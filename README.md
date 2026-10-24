# Harness Portable

一个跨平台的 **SSH 本地端口转发 + 内嵌浏览器** 工具。

在手机上、Windows 上打开局域网/远程 Web 控制台时，不用每次手敲
`ssh -N -L ...`：保存好服务器、端口，以及密码或 SSH 私钥，一键建立隧道并在内置浏览器中打开
`127.0.0.1:port`。

---

## 核心功能

- **SSH 本地端口转发**：等价于 `ssh -N -L local:remoteHost:remotePort user@host`；支持私钥登录（可不填密码），密码错误不会反复重试
- **Windows 自动更新**：Velopack（Setup 安装包和便携 zip 都能更新）；默认使用 Gitee 静态更新索引，也可选「家庭目录」（局域网共享）或 GitHub，各地址都保留，设置里一键切换，并可查看更新日志
- **桌面多隧道并发**：同一进程可同时连接多个服务器，每个隧道独立重连/停止；手机保持一个当前服务
- **连接库**：桌面可折叠侧栏提供全部、常用、最近三种视图和搜索，全部视图按分组展示，连接不重复堆叠；手机合并 SSH 和直连列表，返回列表保持当前服务
- **独立设置**：桌面分类设置窗口、手机设置页；偏好即时保存，更新源明确保存或撤销
- **TOFU 主机密钥**：首次连接记住服务器公钥；密钥变化直接拒绝，防中间人
- **智能主机名解析**：IP / 域名 / Tailscale MagicDNS / NetBIOS 名
- **SSH 配置选择**：Windows/macOS 添加隧道时可从 `~/.ssh/config` 选择别名，自动填充 HostName / User / Port / IdentityFile
- **密码安全存储**：
  - Android：Android Keystore
  - Windows：DPAPI
  - macOS：macOS Keychain
- **内嵌 WebView**：应用内直接打开隧道后的本地端口或任意直连 URL
- **标签页 + 自由分屏**（桌面端）：
  - 拖动标签到窗口边缘分屏
  - 右键标签：复制 / 重命名 / 四向拆分 / 切换为其他隧道或直连
  - 工作区自动保存；命名预设保存与一键恢复（标签名、分屏比例、选中位置）
- **F11 全屏**：只留标签和网页，Esc 退出
- **托盘运行**（可选设置）：关窗不断隧道；默认关窗即退出
- **启动恢复**：默认恢复上次工作区并自动重连已有凭据的 SSH；可在设置中关闭自动重连
- **Windows UI 自动化 API**：按需启用本机接口，查询并操作控件、菜单、弹窗、连接、标签和分屏；新增 UI 受覆盖门禁约束，见 [`docs/windows-automation-api.md`](docs/windows-automation-api.md)。

---

## 平台状态

| 平台 | 状态 | 技术栈 |
|---|---|---|
| Android | ✅ 已可用 | Kotlin + Jetpack Compose + JSch + Android WebView |
| Windows | ✅ 已可用 | .NET 10 WPF + WebView2 + SSH.NET + AvalonDock |
| macOS | 开发中 | SwiftUI + AppKit + WKWebView + Keychain + 系统 SSH 转发 |

---

## 仓库结构

```text
harness-portable/
├─ app/                        # Android 应用（现有，可独立构建）
├─ macos/                      # macOS 原生桌面端（XcodeGen + SwiftUI/AppKit）
│  ├─ HarnessPortable/         # 应用源码
│  └─ HarnessPortableTests/    # macOS 单元测试
├─ windows/                    # Windows 桌面端
│  ├─ HarnessPortable.Windows/ # WPF 应用
│  └─ HarnessPortable.Windows.Tests/
├─ branding/                   # 统一图标源文件与生成脚本
│  ├─ source/                  # SVG 源文件
│  └─ generated/               # PNG / ICO / ICNS
├─ docs/                       # 平台构建与规划文档
├─ spec/                       # 跨平台配置契约
└─ temp/                       # 临时文件（不参与版本控制）
```

---

## Android 构建

环境：JDK 17 + Android SDK（`compileSdk 36`）。

```powershell
# Debug
.\gradlew.bat :app:assembleDebug

# Release（需要 keystore.properties + release.keystore）
.\gradlew.bat :app:assembleRelease
```

产物：

```text
app/build/outputs/apk/debug/app-debug.apk
app/build/outputs/apk/release/app-release.apk
```

详细说明见 [`docs/android-build.md`](docs/android-build.md)。

---

## Windows 构建

环境：.NET 10 SDK，Windows 10/11 x64。

```powershell
# 构建
dotnet build windows/HarnessPortable.Windows.slnx -c Release

# 测试
dotnet test windows/HarnessPortable.Windows.slnx -c Release

# 开发构建
dotnet publish windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o publish/win-x64

# 发布 Windows（Velopack Setup + 便携 zip + 更新源）
pwsh -File scripts/release-windows.ps1

# 发布 Android APK（android.json 更新源，拷到同一 Releases 目录并挂到 GitHub Release）
pwsh -File scripts/release-android.ps1
```

详细说明见 [`docs/windows-build.md`](docs/windows-build.md)。

Windows 配置目录（所有配置均写入系统用户目录，不会在 exe 旁生成文件）：

```text
%APPDATA%\HarnessPortable\
├─ profiles.json      # 隧道与直连列表
├─ secrets.json       # DPAPI 加密的 SSH 密码
├─ known_hosts.json   # TOFU 主机密钥
├─ layouts.json       # 布局预设
├─ workspace.json     # 自动保存的上次工作区
├─ settings.json      # 应用设置
└─ WebView2\          # 浏览器用户数据
```

Windows 开发和隔离测试可用绝对路径环境变量 `HARNESS_PORTABLE_DATA_DIRECTORY` 指定独立数据目录。未设置时仍使用上述目录。

UX 设计、验证记录与试用步骤见 [UX 验证说明](docs/ux-preview-testing.md)。

---

## 配置契约

三个平台共享同一套字段语义，便于未来导入/导出：

- 隧道 profile 字段：`id / name / sshHost / sshPort / user / remoteHost / remotePort / localPort / identityFile`
- 直连 URL 列表
- 主机密钥 TOFU 规则
- 密码凭据与 profile id 关联
- 布局预设（桌面端）：分屏树 + 标签引用 + 尺寸比例

详见 [`spec/config-schema.md`](spec/config-schema.md)。

---

## 品牌图标

统一图标：**深蓝底 + 白色「终端窗口 + 命令提示符」**。

- 底：DeepSeek 蓝 `#4D6BFE`，圆角方形 / 圆形 / 透明（Android 自适应前景）；
- 图形：纯白，一个圆角方框 + `>` 与 `_`；
- 方框 = 打开的那个控制台窗口（也正是内嵌浏览器的那个窗口）；
  提示符 = 敲进去的 `ssh -L` 命令。

一个符号同时说清「SSH 命令行工具」和「窗口里跑」两件事，也避开了
字母图标（没有含义）和钥匙孔（剪影像人形、且与系统钥匙串图标撞车）。

几何只有一处真源 `branding/geometry.js`，源 SVG 由它生成：

```powershell
cd branding
npm install
npm run write-sources   # geometry.js -> source/*.svg
npm run generate        # source/*.svg -> Android mipmap / Windows .ico / macOS icns+appiconset
npm run check           # 校验 source/*.svg 是否与 geometry.js 一致
```

`generate` 会先校验源文件与几何定义一致、并自检圆形与 Android 安全圈约束，
所以不会出现「产物和定义脱节」。生成产物覆盖 Android mipmap、Windows `.ico`、
macOS `.icns` 与 `AppIcon.appiconset`，以及母版 PNG。

设计定稿记录与各尺寸实测见 [`branding/README.md`](branding/README.md)。

---

## macOS / Android

- macOS：构建与远程验收见 [`docs/macos-build.md`](docs/macos-build.md) 和 [`docs/mac-setup.md`](docs/mac-setup.md)；
- Android 多标签 / 折叠屏 / 平板分屏：见 [`docs/android-multipane-plan.md`](docs/android-multipane-plan.md)

---

## Git

```bash
git clone git@github.com:RailgunHamster/HarnessPortable.git
```

- 主分支：`main`
- 忽略项：Android 签名、本地 SDK 路径、.NET 构建产物、`temp/`、`branding/node_modules`


## 许可证与署名

本项目采用标准 [Apache License 2.0](LICENSE)，署名见 [NOTICE](NOTICE)。
Copyright 2026 RailgunHamster。

再分发本项目或其衍生作品时，应按 Apache License 2.0 第 4 条随附许可证、保留相关版权和署名通知，并标明对文件的修改。NOTICE 中相关署名可保留在随附的 NOTICE、源码或文档中，也可放在软件通常显示第三方声明的界面中，不强制要求在界面展示。个人自用无须额外公开声明。本段为说明，具体以 LICENSE 正文为准；第三方组件适用各自的许可证。

代码仓库：[Gitee](https://gitee.com/railgunhamster/HarnessPortable) · [GitHub](https://github.com/RailgunHamster/HarnessPortable)。
Gitee 默认更新索引位于 `updates` 分支；安装包使用发行版附件直链，无需在客户端保存维护者令牌。
发布 Windows / Android 时，发布脚本默认同步 Gitee；可用 `-SkipGitee` 跳过。已构建的两端包也可统一执行 `pwsh -File scripts/release-gitee.ps1 -Platform All`，脚本在匿名下载和 SHA-256 校验通过后才更新公开索引。
