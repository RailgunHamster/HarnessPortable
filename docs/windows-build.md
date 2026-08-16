# Windows 版开发、测试与发布

## 图标

- 源文件：`branding/source/icon.svg`
- 生成：`cd branding && npm install && npm run generate`
- Windows 使用：`windows/HarnessPortable.Windows/Assets/HarnessPortable.ico`

## 技术栈

- .NET 10（WPF）
- WebView2（系统浏览器内核）
- SSH.NET（SSH 本地端口转发）
- Windows DPAPI（密码加密）
- JSON 文件配置（`%APPDATA%\HarnessPortable\`）
- 支持**同时连接多个 SSH 隧道**，每个隧道独立重连/停止
- 单一工作区窗口：隧道与直连聚合为**标签页**，支持拖拽标签到窗口边缘进行**左右/上下分屏**
- 右键标签可**复制标签**：同一隧道/直连可开多个独立 WebView 标签（如网页内切换不同对话）
- **F11 全屏**：隐藏标题栏/工具栏/状态栏，只保留标签和网页，Esc 退出
- 关闭标签不停止隧道；停止隧道才关闭对应标签
- **Home / End** 快速滚到页面顶部/底部（自动寻找可滚动容器）
- 现代化蓝色系 UI（圆角按钮/输入框/卡片）
- 支持**直连多开**：每次点击直连项都会新开一个标签

## 目录

```text
windows/
├─ HarnessPortable.Windows/        # WPF 应用
├─ HarnessPortable.Windows.Tests/  # xUnit 单元测试
└─ HarnessPortable.Windows.slnx
```

## 构建

```powershell
dotnet build windows/HarnessPortable.Windows.slnx -c Release
```

## 测试

```powershell
dotnet test windows/HarnessPortable.Windows.slnx -c Release
```

## 运行（开发）

```powershell
dotnet run --project windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj
```

或直接运行：

```text
windows/HarnessPortable.Windows/bin/Debug/net10.0-windows/HarnessPortable.exe
```

## 发布免安装绿色版

```powershell
dotnet publish windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o publish/win-x64
```

把 `publish/win-x64` 整个目录打成 zip 即绿色版，用户解压双击
`HarnessPortable.exe`，无需安装 .NET。

## 发布单文件 exe（推荐分发）

只产出一个 `HarnessPortable.exe`，发给别人或复制到别的电脑时**只拷这一个文件**
（同目录的 `.pdb` / `.xml` 不需要拷）：

```powershell
dotnet publish windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish/win-x64-single
```

产物：

```text
publish/win-x64-single/HarnessPortable.exe   # 约 170–180 MB，双击即用
```

说明：

- 目标电脑需要 Windows 10/11 x64；
- 不需要安装 .NET；
- 需要 WebView2 Runtime（Win11 预装；Win10 一般随新版 Edge 存在），
  没有的话安装微软官方 “WebView2 Runtime Evergreen”；
- 首次启动会解压，比目录版稍慢；
- 更换平台目标：`-r win-x64` 可改成 `win-arm64`（需要另测）。

## 发布安装包

```powershell
dotnet publish windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj `
  -c Release -r win-x64 --self-contained true -o publish/win-x64
```

之后可用第三方工具（如 Inno Setup）或 `MSIX Packaging Tool` 把
`publish/win-x64` 包装成安装程序。

## 配置文件

- `%APPDATA%\HarnessPortable\profiles.json` —— 隧道与直连列表
- `%APPDATA%\HarnessPortable\secrets.json` —— DPAPI 加密的 SSH 密码
- `%APPDATA%\HarnessPortable\known_hosts.json` —— TOFU 主机密钥
- `%APPDATA%\HarnessPortable\WebView2\` —— 浏览器用户数据

字段约定见 `spec/config-schema.md`。
