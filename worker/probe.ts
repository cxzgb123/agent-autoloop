// 能力探测与 realCLI opt-in 门。探测只跑安全参数（版本/帮助），绝不跑一次会花 model 额度的自检；
// 真实 CLI 跑一次花 model 费用，只有用户明确带 --run 才算授权，脚本用它挡在真跑之前。
import { execFileSync } from "node:child_process";

export type Probe = {
  /** 程序在，且安全参数跑通了 */
  ok: boolean;
  /** --version 这类输出的第一行，能拿到的版本 */
  version: string | null;
  /** 为什么探测不到或跑不通，一行字 */
  error: string | null;
};

/** 用安全参数探一个程序，超时兜底。 */
export function probeCli(program: string, args: string[], timeoutMs = 20_000): Probe {
  try {
    const out = execFileSync(program, args, {
      encoding: "utf8",
      timeout: timeoutMs,
      stdio: ["ignore", "pipe", "pipe"],
    });
    return { ok: true, version: out.trim().split("\n")[0] || null, error: null };
  } catch (error) {
    const e = error as NodeJS.ErrnoException;
    if (e.code === "ENOENT") return { ok: false, version: null, error: `找不到 ${program}` };
    return { ok: false, version: null, error: (e.message ?? String(error)).split("\n")[0] };
  }
}

/** 真实 CLI 跑一次会花 model 费用：只有用户明确带 --run 才算授权。 */
export function realRunOptedIn(argv: string[]): boolean {
  return argv.includes("--run");
}
