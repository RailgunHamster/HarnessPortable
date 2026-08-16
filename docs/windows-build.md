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
- 右键标签可**重命名**，自定义 label 随布局预设保存
- 右键标签可**四向拆分**（复制当前标签到左/右/上/下新 pane），递归组合出三列/2×2/不规则布局
- 右键标签可**切换为其他隧道/直连**，在原布局位置替换当前标签
- **布局预设**：保存当前分屏结构、每个标签对应的隧道/直连，以及拖动后的**窗口比例**，顶部下拉一键切换
- **F11 全屏**：按显示器物理边界覆盖任务栏（含 DPI 换算），Esc 退出
- 关闭主窗口默认**直接退出**；管理页“设置”里可改为“最小化到托盘”
- 启动时默认**自动恢复上次布局**（有预设时直接进入工作区，不显示主界面）
- 关闭标签不停止隧道；停止隧道才关闭对应标签
- DeepSeek Harness 风格 UI：黑底主按钮、白底描边按钮、描边 pill 标签
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
- `%APPDATA%\HarnessPortable\layouts.json` —— 布局预设
- `%APPDATA%\HarnessPortable\settings.json` —— 应用设置（关闭行为等）
- `%APPDATA%\HarnessPortable\WebView2\` —— 浏览器用户数据

> 所有配置都写系统用户目录（`%APPDATA%`），不会在 exe 旁边生成任何文件。

字段约定见 `spec/config-schema.md`。
