// 防的失败方式：能力探测跑了会花 model 额度的命令；没装的 CLI 被当成 ok；`--version` 跑不通也算 ok；
// opt-in 门没有 --run 也放行真实 CLI。探测必须只用安全参数、绝不花钱，opt-in 门必须在用户明确带 --run
// 之前挡住真实跑。
import assert from "node:assert/strict";
import { join } from "node:path";
import { test } from "node:test";
import { probeCli, realRunOptedIn } from "../worker/probe.ts";

const fake = join(import.meta.dirname, "fixtures", "fake-cli.mjs");

test("probeCli 只跑安全参数，拿到版本", () => {
  const probe = probeCli(process.execPath, [fake, "--version"]);
  assert.equal(probe.ok, true);
  assert.equal(probe.version, "fake-cli 1.2.3");
  assert.equal(probe.error, null);
});

test("probeCli 找不到程序报 not ok", () => {
  const probe = probeCli(join(import.meta.dirname, "没有这个程序"), []);
  assert.equal(probe.ok, false);
  assert.ok(probe.error);
});

test("probeCli 程序在但安全参数跑不通，不算 ok", () => {
  const probe = probeCli(process.execPath, [fake, "--别的参数"]);
  assert.equal(probe.ok, false);
  assert.ok(probe.error);
});

test("opt-in 门：没有 --run 不放行，带了才放行", () => {
  assert.equal(realRunOptedIn([]), false);
  assert.equal(realRunOptedIn(["codex"]), false);
  assert.equal(realRunOptedIn(["codex", "--run"]), true);
});
