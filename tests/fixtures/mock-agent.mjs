// 跨平台假 Agent（文档 16 节 tests/fixtures/mock-agent.mjs）：不依赖 sh、不依赖 chmod，直接 `node mock-agent.mjs`。
// 对 use: "command" 的 agent，stdout 的最后一行就是答案；本假 Agent 打印一行答案并退出 0。
// 它读 AGENT_AUTOLOOP_CONTEXT 证明上下文文件真的传到了，读不到就报失败。
import { readFileSync } from "node:fs";

const context = process.env.AGENT_AUTOLOOP_CONTEXT ?? "";
if (!context || !readFileSync(context, "utf8")) {
  console.error("mock-agent: 拿不到上下文文件");
  process.exit(2);
}

console.log("改好了");
