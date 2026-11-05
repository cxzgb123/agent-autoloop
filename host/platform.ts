// 平台差异：程序发现、路径展开、进程启动/停止。内核与协议不碰这里；只有 host/worker 用。
// 文档 16 节把平台接口拆成 path / program discovery / control channel / stop 四块；Phase 1 只落地
// worker 需要的 launch / alive / signal / groupAlive，控制通道仍在 WinHost（Phase 2）。
// Windows 上进程树强杀归 Job Object（WinHost），这里 stop 只杀单进程：PID 复用与整树清理是 Phase 2 的账。
import { execFileSync, spawn } from "node:child_process";
import { closeSync, openSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";

const isWindows = process.platform === "win32";

/** 展开路径里的 ~ 开头（~/ 或 ~\）。 */
export function expandHome(cwd: string): string {
  return cwd.replace(/^~(?=[/\\])/, homedir());
}

/** 命令名 -> 完整路径，找不到返回 null。POSIX 走登录 shell 的 command -v，Windows 走 where.exe。 */
export function findProgram(name: string): string | null {
  if (!isWindows) {
    const shell = process.env.SHELL ?? "/bin/sh";
    try {
      const printed = execFileSync(shell, ["-lc", `command -v ${name}`], {
        encoding: "utf8",
        timeout: 15_000,
        stdio: ["ignore", "pipe", "ignore"],
      });
      return printed.trim().split("\n").at(-1) || null;
    } catch {
      return null;
    }
  }
  try {
    const printed = execFileSync("where", [name], { encoding: "utf8", timeout: 15_000, stdio: ["ignore", "pipe", "ignore"] });
    return printed.trim().split(/\r?\n/)[0] || null;
  } catch {
    return null;
  }
}

const quoted = (word: string) => `'${word.replaceAll("'", `'\\''`)}'`;

export type LaunchFiles = { out: string; err: string; exit: string; command: string };

/** 启动一个 detached agent，输出与退出码落盘。返回 pid。 */
export function launch(
  argv: string[],
  cwd: string,
  env: NodeJS.ProcessEnv,
  files: LaunchFiles,
  marker: string,
): number {
  if (!isWindows) return launchPosix(argv, cwd, env, files, marker);
  return launchWindows(argv, cwd, env, files);
}

/** 这个进程还是我们启动的那个吗（marker 在命令行里）：true 在跑、false 已没、null 无法查看。 */
export function alive(pid: number | undefined, marker: string): boolean | null {
  if (!pid) return false;
  if (!isWindows) {
    try {
      const command = execFileSync("ps", ["-p", String(pid), "-o", "command="], { encoding: "utf8" });
      return command.includes(marker);
    } catch (error) {
      // ps 退出码 1 = 进程没了；其他（EPERM 等）= 无法查看，不是没了。
      const status = (error as { status?: number | null }).status;
      return status === 1 ? false : null;
    }
  }
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    return code === "ESRCH" ? false : null;
  }
}

/** 给整棵进程树发信号（SIGTERM 优雅，SIGKILL 强停）。Windows 只杀单进程。 */
export function signal(pid: number | undefined, name: "SIGTERM" | "SIGKILL"): void {
  if (!pid) return;
  try {
    process.kill(isWindows ? pid : -pid, name);
  } catch {}
}

/** 进程组里还有没有非僵尸成员（wrapper 先死、子进程还活）：true 在跑、false 已没、null 无法查看。 */
export function groupAlive(pid: number | undefined): boolean | null {
  if (!pid) return false;
  if (!isWindows) {
    try {
      return execFileSync("ps", ["-ax", "-o", "pgid=", "-o", "stat="], { encoding: "utf8" })
        .trim()
        .split("\n")
        .some((line) => {
          const [group, state] = line.trim().split(/\s+/);
          return Number(group) === pid && !state.startsWith("Z");
        });
    } catch {
      return null;
    }
  }
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    return code === "ESRCH" ? false : null;
  }
}

// POSIX：登录 shell 给 PATH 与签名；命令放在文件里避免安装路径里的字符被当作 shell 源码；wrapper 写退出码。
function launchPosix(argv: string[], cwd: string, env: NodeJS.ProcessEnv, files: LaunchFiles, marker: string): number {
  writeFileSync(files.command, `unset AGENT_AUTOLOOP_COMMAND\nexec ${argv.map(quoted).join(" ")}\n`, { mode: 0o600 });
  const script = [
    "out=$1",
    "err=$2",
    "result=$3",
    "shift 3",
    "umask 077",
    '"$@" >"$out" 2>"$err"',
    'echo $? >"$result.tmp"',
    'mv "$result.tmp" "$result"',
    ":",
  ].join("\n");
  const login = [process.env.SHELL ?? "/bin/sh", "-lc", 'exec /bin/sh "$AGENT_AUTOLOOP_COMMAND"'];
  const child = spawn("/bin/sh", ["-c", script, marker, files.out, files.err, files.exit, ...login], {
    cwd,
    env: { ...env, AGENT_AUTOLOOP_COMMAND: files.command },
    detached: true,
    stdio: "ignore",
  });
  child.on("error", () => {});
  child.unref();
  return child.pid!;
}

// Windows：无 shell，用 detached 的 node 包装进程跑命令、写退出码。包装进程的最后一步是写退出码并
// 退出，所以「进程死」即「退出码已落盘」，与 POSIX wrapper 的语义一致；父进程（worker）崩溃不丢退出码。
// 真正的进程树/身份记账归 WinHost（ProcessSupervisor 已有 WaitForSingleObject+GetExitCodeProcess），Phase 2 落地。
const WRAPPER = `const{spawn}=require('node:child_process');const{writeFileSync}=require('node:fs');` +
  `const[program,...args]=JSON.parse(process.env.AGENT_AUTOLOOP_ARGV);` +
  `const child=spawn(program,args,{stdio:'inherit'});` +
  `child.on('error',()=>{try{writeFileSync(process.env.AGENT_AUTOLOOP_EXIT,'1')}catch{}process.exit(1)});` +
  `child.on('exit',(code)=>{try{writeFileSync(process.env.AGENT_AUTOLOOP_EXIT,String(code??1))}catch{}process.exit(code??1)});`;

function launchWindows(argv: string[], cwd: string, env: NodeJS.ProcessEnv, files: LaunchFiles): number {
  const outFd = openSync(files.out, "w");
  const errFd = openSync(files.err, "w");
  const child = spawn(process.execPath, ["-e", WRAPPER], {
    cwd,
    env: {
      ...env,
      AGENT_AUTOLOOP_ARGV: JSON.stringify(argv),
      AGENT_AUTOLOOP_EXIT: files.exit,
    },
    detached: true,
    stdio: ["ignore", outFd, errFd],
  });
  closeSync(outFd);
  closeSync(errFd);
  child.unref();
  return child.pid!;
}
