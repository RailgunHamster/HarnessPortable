# Harness Portable

一个跨平台的 **SSH 本地端口转发 + 内嵌浏览器** 工具。

在手机上、Windows 上打开局域网/远程 Web 控制台时，不用每次手敲
`ssh -N -L ...`：保存好服务器、端口和密码，一键建立隧道并在内置浏览器中打开
`127.0.0.1:port`。

---

## 核心功能

- **SSH 本地端口转发**：等价于 `ssh -N -L local:remoteHost:remotePort user@host`
- **多隧道并发**：同一进程可同时连接多个服务器，每个隧道独立重连/停止
- **TOFU 主机密钥**：首次连接记住服务器公钥；密钥变化直接拒绝，防中间人
- **智能主机名解析**：IP / 域名 / Tailscale MagicDNS / NetBIOS 名
- **密码安全存储**：
  - Android：Android Keystore
  - Windows：DPAPI
  - macOS（规划）：Keychain
- **内嵌 WebView**：应用内直接打开隧道后的本地端口或任意直连 URL
- **标签页 + 自由分屏**（桌面端）：
  - 拖动标签到窗口边缘分屏
  - 右键标签：复制 / 重命名 / 四向拆分 / 切换为其他隧道或直连
  - 布局预设保存与一键恢复（包含管理页位置、标签名、分屏比例）
- **F11 全屏**：只留标签和网页，Esc 退出
- **托盘运行**（可选设置）：关窗不断隧道；默认关窗即退出
- **启动恢复**：默认启动时自动恢复上次使用的布局预设

---

## 平台状态

| 平台 | 状态 | 技术栈 |
|---|---|---|
| Android | ✅ 已可用 | Kotlin + Jetpack Compose + JSch + Android WebView |
| Windows | ✅ 已可用 | .NET 10 WPF + WebView2 + SSH.NET + AvalonDock |
| macOS | 🚧 规划中 | SwiftUI + WKWebView + NMSSH/libssh2（见 `docs/mac-setup.md`） |

---

## 仓库结构

```text
harness-portable/
├─ app/                        # Android 应用（现有，可独立构建）
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

# 免安装目录版
dotnet publish windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o publish/win-x64

# 单文件版（推荐分发）
dotnet publish windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish/win-x64-single
```

详细说明见 [`docs/windows-build.md`](docs/windows-build.md)。

Windows 配置目录（所有配置均写入系统用户目录，不会在 exe 旁生成文件）：

```text
%APPDATA%\HarnessPortable\
├─ profiles.json      # 隧道与直连列表
├─ secrets.json       # DPAPI 加密的 SSH 密码
├─ known_hosts.json   # TOFU 主机密钥
├─ layouts.json       # 布局预设
├─ settings.json      # 应用设置
└─ WebView2\          # 浏览器用户数据
```

---

## 配置契约

三个平台共享同一套字段语义，便于未来导入/导出：

- 隧道 profile 字段：`id / name / sshHost / sshPort / user / remoteHost / remotePort / localPort`
- 直连 URL 列表
- 主机密钥 TOFU 规则
- 密码凭据与 profile id 关联
- 布局预设（桌面端）：分屏树 + 标签引用 + 尺寸比例

详见 [`spec/config-schema.md`](spec/config-schema.md)。

---

## 品牌图标

统一图标：**“H + SSH 隧道 + 钥匙孔”**。

- 两条白色竖条：H / 本机与远端
- 中间青色横管：SSH 隧道
- 中心钥匙孔：密码与主机密钥保护
- 深蓝渐变底：网络/技术工具质感

源文件与生成方式见 [`branding/README.md`](branding/README.md)：

```powershell
cd branding
npm install
npm run generate
```

生成产物覆盖 Android mipmap、Windows `.ico`、macOS `.icns` 以及母版 PNG。

---

## macOS / Android 后续规划

- macOS：见 [`docs/mac-setup.md`](docs/mac-setup.md)（远程构建准备步骤）
- Android 多标签 / 折叠屏 / 平板分屏：见 [`docs/android-multipane-plan.md`](docs/android-multipane-plan.md)

---

## Git

```bash
git clone git@github.com:RailgunHamster/HarnessPortable.git
```

- 主分支：`main`
- 忽略项：Android 签名、本地 SDK 路径、.NET 构建产物、`temp/`、`branding/node_modules`
