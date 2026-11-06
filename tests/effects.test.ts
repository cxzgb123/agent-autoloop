// Effect requests: a worker asks to do something with a side effect. Failure modes: a worker's receipt carries an
// approval of its own effect (self-approval); an effect executes without the owner answering; the raw parameters of an
// effect leak into the card or the row; the same effect is recorded twice or re-approved; a request whose parameters
// changed still rides on the old approval; the owner's answer approves a different effect than the one asked. Unknown
// side effects are recorded once and never redone from the kernel's side; reconciling what actually ran is the executor's
// job, not a re-request.
import assert from "node:assert/strict";
import { test } from "node:test";
import { at, setup } from "./helpers.ts";

setup("effects");
const { ingest, claimNext, finish, answer, execute } = await import("../host/runtime.ts");
const { InputError } = await import("../kernel/errors.ts");
const { one, run } = await import("./storage.ts");

const clean = () => {
  for (const table of ["run_events", "runs", "events", "seen", "cards", "deliveries", "timers", "effects"])
    run(`DELETE FROM ${table}`);
};
const note = (n: number, step = "new") => ({ id: `feedback:${n}:${step}`, type: `feedback.${step}`, subject: `#${n}` });
const effect = (over: Record<string, unknown> = {}) => ({
  key: "merge-x",
  kind: "merge",
  target: "owner/repo#12",
  parameters: { repo: "owner/repo", pr: "12", head: "abc123" },
  expectedState: "merged",
  reason: "审查通过，可以合并",
  ...over,
});

/** A full run whose receipt asks for the effect; returns the run and what the hub rejected. */
function request(n: number, now = at(0), over: Record<string, unknown> = {}) {
  ingest("app", note(n), now);
  const job = claimNext("w1", `c${Math.random()}`, now)!;
  const ack = finish(job.run, job.token, { outcome: "done", summary: "要执行一个动作", effect: [effect(over)] }, now);
  return { job, rejected: ack.rejected };
}
const effectRow = (key: string) => one<any>("SELECT * FROM effects WHERE key = ?", [key]);
const cardOf = (key: string) => one<any>("SELECT * FROM cards WHERE key = ?", [`effect:${key}`]);

test("a worker's effect request is recorded as requested and waits for the owner; its raw parameters never appear", () => {
  clean();
  request(1);
  const row = effectRow("merge-x");
  assert.equal(row.state, "requested");
  assert.equal(row.principal, null);
  assert.equal("parameters" in row, false, "raw parameters must not be stored, only their hash");
  const card = cardOf("merge-x");
  assert.equal(card.state, "open");
  assert.ok(!card.text.includes("abc123"), "a parameter value leaked into the card");
});

test("only the owner's answer approves; declining leaves it declined", () => {
  clean();
  request(1);
  answer(cardOf("merge-x").id, { choice: 0, by: "本人" }, at(1));
  const approved = effectRow("merge-x");
  assert.equal(approved.state, "approved");
  assert.equal(approved.principal, "本人");
  assert.ok(approved.approved_at);
  // Approving wakes no new run: the executor does the effect, not the agent that asked for it.
  assert.equal(one<{ n: number }>("SELECT COUNT(*) AS n FROM runs WHERE state = 'queued'")!.n, 0);

  clean();
  request(2, at(0), { key: "merge-y" });
  answer(cardOf("merge-y").id, { choice: 1, by: "本人" }, at(1));
  assert.equal(effectRow("merge-y").state, "declined");
});

test("the same effect is recorded once and not re-approved", () => {
  clean();
  request(1);
  const second = request(2);
  assert.deepEqual(second.rejected, []);
  assert.equal(effectRow("merge-x").state, "requested");
  assert.equal(one<{ n: number }>("SELECT COUNT(*) AS n FROM effects WHERE key = 'merge-x'")!.n, 1);
  assert.equal(one<{ n: number }>("SELECT COUNT(*) AS n FROM cards WHERE key = 'effect:merge-x'")!.n, 1);
});

test("a request whose parameters changed must be re-approved; the old approval doesn't carry over", () => {
  clean();
  request(1);
  const changed = request(2, at(1), { parameters: { repo: "owner/repo", pr: "12", head: "def456" } });
  assert.equal(changed.rejected.length, 1);
  assert.equal(changed.rejected[0].what, "effect");
  assert.match(changed.rejected[0].reason, /重新批准/);
  assert.equal(effectRow("merge-x").state, "requested");
  assert.equal(cardOf("merge-x").state, "open");

  // A change to the target (not the parameters) also voids the old approval: the action is no longer what was approved.
  request(3, at(2), { target: "owner/repo#13" });
  assert.equal(effectRow("merge-x").state, "requested");
});

test("a worker receipt can't mark its own effect approved", () => {
  clean();
  ingest("app", note(1), at(0));
  const job = claimNext("w1", "c", at(0))!;
  assert.throws(
    () => finish(job.run, job.token, { outcome: "done", effect: [{ ...effect(), approved: true }] }, at(0)),
    InputError,
  );
  assert.throws(() => finish(job.run, job.token, { outcome: "done", approved: true }, at(0)), InputError);
});

test("a vague effect with no concrete target is refused, not approved by default", () => {
  clean();
  ingest("app", note(1), at(0));
  const job = claimNext("w1", "c", at(0))!;
  assert.throws(
    () => finish(job.run, job.token, { outcome: "done", effect: [{ ...effect(), target: "" }] }, at(0)),
    InputError,
  );
  assert.throws(
    () => finish(job.run, job.token, { outcome: "done", effect: [{ ...effect(), kind: "" }] }, at(0)),
    InputError,
  );
  // Nothing vague got a card or a row: the broker refuses to approve anything it can't state concretely.
  assert.equal(one<{ n: number }>("SELECT COUNT(*) AS n FROM effects")!.n, 0);
});

test("an unknown effect is never redone: after execution the same request doesn't re-issue", () => {
  clean();
  request(1);
  answer(cardOf("merge-x").id, { choice: 0, by: "本人" }, at(1));
  // The executor reported the outcome is unknown: the effect must not be re-requested or re-approved.
  execute("effectUnknown", ["merge-x"], at(2));
  const again = request(3);
  assert.deepEqual(again.rejected, []);
  assert.equal(effectRow("merge-x").state, "unknown");
  assert.equal(one<{ n: number }>("SELECT COUNT(*) AS n FROM effects WHERE key = 'merge-x'")!.n, 1);
});

test("an approval is one-shot: a consumed approval can't be reused by a later request", () => {
  clean();
  request(1);
  answer(cardOf("merge-x").id, { choice: 0, by: "本人" }, at(1));
  assert.equal(effectRow("merge-x").state, "approved");
  // The same worker handing the same effect in again must not re-consume the one-shot approval.
  const again = request(2);
  assert.deepEqual(again.rejected, []);
  assert.equal(effectRow("merge-x").state, "approved");
  assert.equal(one<{ n: number }>("SELECT COUNT(*) AS n FROM effects WHERE key = 'merge-x'")!.n, 1);
});
