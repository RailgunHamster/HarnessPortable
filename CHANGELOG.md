# Changelog

## 2.5.1

- 便携 zip 与 Setup 安装包一样支持自动更新（解压后运行根目录的 `Harness Portable.exe`）。无法更新的只有开发运行和旧版单文件 exe。

## 2.5.0

- Windows 使用 Velopack 自动更新。设置里可查看并修改更新服务器、阅读更新日志、检查并安装新版本。默认更新源为局域网 `\\server-home\public\Software\HarnessPortable-Releases`，也可改为 GitHub 仓库地址。
- 支持 SSH 私钥登录（可不填密码）；从 `~/.ssh/config` 读取 IdentityFile。密码错误只尝试一次，不再反复重连以免锁死账号。
