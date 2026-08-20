# macOS 工程

这是 Harness Portable 的原生 SwiftUI/AppKit 版本。工程文件由 XcodeGen 生成，源码可以在 Windows 上维护，编译和 UI 验收在 Mac 上执行。

## 生成与构建

```bash
cd macos
xcodegen generate
xcodebuild -project HarnessPortable.xcodeproj \
  -scheme HarnessPortable \
  -configuration Debug \
  -destination 'platform=macOS' \
  -derivedDataPath build build
```

测试：

```bash
xcodebuild -project HarnessPortable.xcodeproj \
  -scheme HarnessPortable \
  -destination 'platform=macOS' \
  -derivedDataPath build test
```

## 当前实现

- SwiftUI 管理页与原生菜单栏入口；
- 一个工作区窗口，支持标签、复制、重命名、关闭、目标替换；
- 递归左右/上下分屏，分隔条可拖动并保存比例；
- 每个标签独立 `WKWebView`，使用持久化 WebKit 数据；
- `profiles.json`、`layouts.json`、`settings.json` 与 Windows 字段兼容；
- `http://127.0.0.1` 和局域网 HTTP 由应用 Info.plist 的网络策略允许；正式上架前应把 ATS 例外收窄并重新验证；
- SSH 密码可在 profile 编辑器中保存/清除，实际存储在 macOS Keychain；
- TOFU 主机密钥保存到 `known_hosts.json`，连接前用 `ssh-keyscan` 校验；
- 每个 profile 一个系统 SSH 转发进程，支持端口递增和断线重连。

系统 SSH 方案不把密码写入命令行。应用通过 Security API 从 Keychain 读取密码，并在当前进程内缓存刚保存的密码，写入仅当前用户可读的临时文件，再由 `/usr/bin/ssh` 的 askpass 脚本读取；隧道停止或进程退出时删除临时文件。

配置目录：

```text
~/Library/Application Support/HarnessPortable/
├── profiles.json
├── known_hosts.json
├── layouts.json
├── settings.json
└── Support/                  # askpass 与临时 host 文件
```

`WKWebView` 使用系统持久化 data store，数据目录由 WebKit 管理。
