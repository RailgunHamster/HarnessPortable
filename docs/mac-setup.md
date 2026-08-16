# Mac 版搭建与远程构建准备

> 目的：Windows 版先交付；Mac 版后续开发。开发机（Windows）通过 SSH 远程使用你的 Mac 进行
> 编译、测试与验收。本文档是你在 Mac 上需要完成的一次性准备工作。

## 1. 硬性条件

- macOS 13（Ventura）或更新版本，Apple Silicon 或 Intel 均可（请告知架构）。
- 已安装 **完整 Xcode**（App Store 下载，不是只装 Command Line Tools）。
- 已接受 Xcode 许可：

```bash
sudo xcodebuild -license accept
```

- 已安装 Homebrew（如未安装：https://brew.sh/）。

## 2. 安装远程构建工具

```bash
brew install xcodegen
```

`xcodegen` 用于从 `project.yml` 生成 `.xcodeproj`，这样项目文件可以在 Windows 上维护，
在 Mac 上生成并编译。

## 3. 网络打通（推荐 Tailscale）

两台机器都安装 Tailscale 并登录同一个账号：

- Mac：https://tailscale.com/download
- Windows 开发机：由我这边处理。

登录后在 Tailscale 控制台或 Mac 终端执行：

```bash
tailscale status
```

记录 Mac 的 MagicDNS 名字，形如：

```text
your-mac.your-tailnet.ts.net
```

如果你和 Mac 在同一个局域网，也可以直接用局域网 IP。

## 4. 开启 SSH 远程登录

系统设置 → 通用 → 共享 → 打开 **远程登录（Remote Login）**。

允许范围建议只勾选你当前使用的用户；如果希望更安全，可以新建一个标准用户（非管理员）
专门用于构建。

命令行等价操作：

```bash
sudo systemsetup -setremotelogin on
```

## 5. 添加开发机的 SSH 公钥

在你的 Mac 终端执行：

```bash
mkdir -p ~/.ssh && chmod 700 ~/.ssh
echo 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIHl97+oPXAs23vRyBAyJoJI9f7816CIxW0BYKDZV3eMN harness-mac-build' >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
```

> 私钥只存在于 Windows 开发机，不会通过聊天或仓库传输。

## 6. 防止 Mac 睡眠

远程构建期间 Mac 需要保持唤醒：

```bash
sudo pmset -a sleep 0
```

笔记本请保持盒盖打开并接电源。构建完成后可恢复默认：

```bash
sudo pmset -a sleep 1
```

## 7. 把连接信息发给我

```text
用户名@host
```

例如：

```text
alice@your-mac.your-tailnet.ts.net
```

同时告诉我：

- Mac 是 Apple Silicon 还是 Intel；
- 是否已安装完整 Xcode、Homebrew、xcodegen。

我会第一时间从 Windows 执行以下验证：

```bash
ssh 用户名@host xcodebuild -version
ssh 用户名@host xcodegen --version
ssh 用户名@host uname -m
```

## 8. 后续开发方式（打通后）

1. 我把 Swift/SwiftUI 源码和 `project.yml` 传到 Mac（git 或 scp）。
2. 在 Mac 上执行：

```bash
cd harness-portable/macos
xcodegen generate
xcodebuild -project HarnessPortable.xcodeproj \
  -scheme HarnessPortable \
  -configuration Debug \
  -derivedDataPath build \
  build
```

3. 编译错误和测试结果我直接通过 SSH 读取并迭代。
4. UI 验收阶段再开启屏幕共享（系统设置 → 通用 → 共享 → 屏幕共享）。

## 9. 技术约定（Mac 版）

- UI：SwiftUI + AppKit（WKWebView 通过 `NSViewRepresentable` 嵌入）。
- 图标：使用 `branding/generated/HarnessPortable.icns`，源文件与生成方式见
  `branding/README.md`。
- SSH：NMSSH / libssh2 系，实现本地端口转发。
- 配置：与 Windows 版共用 `spec/config-schema.md` 定义的 JSON 约定。
- 密码：macOS Keychain。
- 已知主机密钥：TOFU，行为对齐 Windows 版 `KnownHostsStore`。
- 后台：菜单栏常驻（MenuBarExtra / NSStatusItem），关窗不断隧道。
- **多隧道**：桌面端必须支持同时连接多个 profile，每个 profile 独立
  session/状态/浏览器窗口，菜单栏可分别断开；直连项每点击一次新开一个
  窗口；行为对齐 Windows 版 `TunnelManager`。

## 10. 签名与公证（最后阶段）

本地开发可用 ad-hoc 签名运行。对外分发需要：

- Apple Developer 账号；
- Developer ID Application 证书；
- `codesign` + `notarytool` 公证；
- 如走 App Store 则另行准备 Sandbox 与审核材料。

这些可以等 Windows 版验收完成后再处理。
