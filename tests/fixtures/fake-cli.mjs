// 假 CLI：给能力探测用，绝不花 model 费用。--version 打印一行版本；其他参数退出 3。
const argv = process.argv.slice(2);
if (argv.includes("--version")) {
  console.log("fake-cli 1.2.3");
  process.exit(0);
}
process.stderr.write("fake-cli: 不支持这个参数\n");
process.exit(3);
