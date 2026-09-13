# macOS 构建与测试

## 环境

- macOS 13 或更高版本；
- 完整 Xcode（不是只有 Command Line Tools）；
- XcodeGen：`brew install xcodegen`；
- Apple Silicon 或 Intel 均可，首次先执行 `xcodebuild -license accept`。

## 构建

```bash
cd macos
xcodegen generate
xcodebuild -project HarnessPortable.xcodeproj \
  -scheme HarnessPortable \
  -configuration Debug \
  -destination 'platform=macOS' \
  -derivedDataPath build build
```

## 单元测试

```bash
xcodebuild -project HarnessPortable.xcodeproj \
  -scheme HarnessPortable \
  -destination 'platform=macOS' \
  -derivedDataPath build test
```

测试覆盖配置契约默认值、Windows PascalCase/camelCase JSON 读取、URL 规范化、嵌套布局恢复和主机地址分类。

## 运行

```bash
open build/Build/Products/Debug/HarnessPortable.app
```

当前 Mac 端配置目录：

```text
~/Library/Application Support/HarnessPortable/
├── profiles.json
├── known_hosts.json
├── layouts.json
├── settings.json
└── Support/                  # askpass 与临时 host 文件
```

`WKWebView` 使用系统持久化 data store，数据目录由 WebKit 管理。

SSH 密码可在 profile 编辑器中保存或清除，不写入 JSON，而是保存在 Keychain：service 为 `com.harness.portable`，account 为 tunnel profile id。应用通过 Security API 读取 Keychain，并在当前进程内缓存刚保存的密码；隧道运行期间创建仅当前用户可读的临时 askpass 密码文件，停止或退出时删除。未保存密码时使用公钥（profile 的 `identityFile`、`~/.ssh` 默认身份或 ssh-agent），并以 `BatchMode` 避免卡在密码提示。转发由系统 `/usr/bin/ssh` 进程负责，每个 profile 独立运行，端口从 profile 的 `localPort` 起最多尝试 10 个端口。认证失败立即停止；只有曾经连通后的断线才按 3 到 30 秒退避重连。

## 远程构建

Windows 端配置好 Mac Remote Login 和 `harness_mac_ed25519` 公钥后，可从仓库根目录执行：

```powershell
$key = 'C:\Windows\System32\config\systemprofile\.ssh\harness_mac_ed25519'
ssh -i $key -o IdentitiesOnly=yes RailgunHamster@macair `
  "cd harness-portable/macos && xcodegen generate && xcodebuild -project HarnessPortable.xcodeproj -scheme HarnessPortable -configuration Debug -destination 'platform=macOS' build"
```

正式签名、公证、睡眠唤醒、局域网 HTTP/Tailscale 解析和 SSH 键盘交互认证必须在真实 Mac 上继续验收。
