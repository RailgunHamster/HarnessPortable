# Windows 版开发、测试与发布

## 技术栈

- .NET 10（WPF）
- WebView2（系统浏览器内核）
- SSH.NET（SSH 本地端口转发）
- Windows DPAPI（密码加密）
- JSON 文件配置（`%APPDATA%\HarnessPortable\`）
- 支持**同时连接多个 SSH 隧道**，每个隧道独立重连/停止，并可有各自的浏览器窗口
- 支持**直连多开**：每次点击直连项都会新开一个独立浏览器窗口

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
