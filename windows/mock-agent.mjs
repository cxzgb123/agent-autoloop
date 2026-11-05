// 假 Agent：模拟一轮 agent run，零依赖，直接 `node mock-agent.mjs` 启动，无 shell、无 chmod。
// 两种模式：
//   normal（默认）：spawn 一个子进程做点事，等它退出，自己打印结果退出 0。
//   hang：spawn 一个永不退出的子进程，自己也不退出，用于测 TerminateJobObject 清整棵进程树。
// 进程树是 Job Object 监督的核心证据：子进程默认继承父进程所在的 Job。
import { spawn } from "node:child_process";

const mode = process.env.MOCK_MODE ?? "normal";
const run = process.env.MOCK_RUN ?? "0";

const child = spawn(
  process.execPath,
  ["-e", mode === "hang" ? "setInterval(() => {}, 1000)" : "console.log('child work done')"],
  { stdio: "inherit" },
);

if (mode === "hang") {
  // 父子都挂住，等 WinHost 用 TerminateJobObject 清树。
  setInterval(() => {}, 1000);
} else {
  child.on("error", (error) => {
    console.error(`child failed: ${error.message}`);
    process.exit(2);
  });
  child.on("exit", (code) => {
    console.log(`agent done: run=${run} child exit ${code}`);
    process.exit(code ?? 0);
  });
}
