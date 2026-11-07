// 假 toast 命令：只把参数逐行打印，不弹真通知。真实通知需 Windows App SDK 注册/激活（NOT_RUN）。
// 由 winhost notify 用 ProcessStartInfo.ArgumentList 调用，不经过 shell；标题里的任何字符都是数据。
process.stdout.write(process.argv.slice(2).join("\n") + "\n");
