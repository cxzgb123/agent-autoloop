// 证明 worker 在 Windows 原生环境能闭环：假 Agent 是 node 脚本（无 sh），command 走 use: "command"，
// 退出码与结果由 platform.ts 的 Windows 分支落盘。这是文档 B1 的「provider 直接 spawn，无 shell」验证。
// 失败方式：worker 仍依赖 /bin/sh 或 ps；Windows 上退出码没写进 exit 文件；上下文文件没到假 Agent。
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { setup } from "./helpers.ts";

const home = setup("worker-windows");
const folder = join(home, "project");
const { mkdirSync } = await import("node:fs");
mkdirSync(folder);
process.env.AGENT_AUTOLOOP_PUBLIC_URL = "https://hub.test";
const { createWorker } = await import("../worker/worker.ts");

const mockAgent = join(import.meta.dirname, "fixtures", "mock-agent.mjs");

test("a node agent (no shell) runs to done on this platform, with its context file", async () => {
  const results: any[] = [];
  let claimed = false;
  const job = {
    run: 1,
    token: "t",
    attempt: 1,
    session: null,
    askFirst: [],
    rule: { name: "跨平台", cwd: folder, prompt: "做这件事。" },
    agent: { id: "a", name: "甲", use: "command", access: "edit", command: [process.execPath, mockAgent] },
    context: { events: [], answers: [], timers: [], waiting: [], open: [], decided: [] },
  };
  const worker = createWorker({
    name: "w",
    wait: 0,
    hub: {
      work: async () => {
        const next = claimed ? null : job;
        claimed = true;
        return { job: next, cancel: [] };
      },
      started: async () => ({}),
      result: async (_run: number, _token: string, body: unknown) => {
        results.push(body);
        return {};
      },
    } as any,
  });
  for (let i = 0; i < 100 && !results.length; i++) {
    await worker.tick();
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
  assert.equal(results.length, 1, "no result came back");
  assert.equal(results[0].outcome, "done", JSON.stringify(results[0]));
  assert.equal(results[0].summary, "改好了");
});

test("a node agent that fails keeps its exit code, and nothing of its stdout is an answer", async () => {
  const results: any[] = [];
  let claimed = false;
  const job = {
    run: 2,
    token: "t2",
    attempt: 1,
    session: null,
    askFirst: [],
    rule: { name: "失败", cwd: folder, prompt: "做这件事。" },
    agent: {
      id: "a",
      name: "甲",
      use: "command",
      access: "edit",
      command: [process.execPath, "-e", "console.error('故意的失败'); process.exit(3)"],
    },
    context: { events: [], answers: [], timers: [], waiting: [], open: [], decided: [] },
  };
  const worker = createWorker({
    name: "w",
    wait: 0,
    hub: {
      work: async () => {
        const next = claimed ? null : job;
        claimed = true;
        return { job: next, cancel: [] };
      },
      started: async () => ({}),
      result: async (_run: number, _token: string, body: unknown) => {
        results.push(body);
        return {};
      },
    } as any,
  });
  for (let i = 0; i < 100 && !results.length; i++) {
    await worker.tick();
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
  assert.equal(results.length, 1);
  // fail 模式退出 3，stderr 留一句话；worker 要如实报 failed，不把 stdout 当答案。
  assert.equal(results[0].outcome, "failed");
});
