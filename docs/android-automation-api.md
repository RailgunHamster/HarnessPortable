# Android 原生自动化 API

APK 使用原生 Compose 语义接口，支持连接库、连接编辑、设置、WebDAV、应用弹窗和嵌入网页操作。与 Windows 一样，UI 和 API 调用同一项应用操作；不通过反射调用私有事件，也不模拟屏幕坐标。macOS 尚未提供运行时自动化 API。

普通启动不会监听端口。API 需要已授权的 ADB 会话显式启动，接收器受 Android `android.permission.DUMP` 权限保护。设备端只监听 `127.0.0.1`，电脑通过 `adb forward` 连接，每条请求都必须携带随机的 256 位会话令牌。API 拥有与使用者相同的操作能力，只应向可信测试程序提供端点文件。

## 启动与调用

需要 Android platform-tools 和 PowerShell 7。先通过 USB 或无线调试连接设备，再执行：

```powershell
$session = ./scripts/Start-HarnessAndroidApi.ps1 -Serial '192.168.0.115:40031'
$endpoint = $session.EndpointFile
./scripts/Invoke-HarnessAndroidApi.ps1 -EndpointFile $endpoint -Method api.describe
./scripts/Invoke-HarnessAndroidApi.ps1 -EndpointFile $endpoint -Method library.list
./scripts/Invoke-HarnessAndroidApi.ps1 -EndpointFile $endpoint -Method network.resolve `
  -Arguments '{"host":"railgunhamster"}' -Wait
./scripts/Invoke-HarnessAndroidApi.ps1 -EndpointFile $endpoint -Method app.settings -Wait
./scripts/Invoke-HarnessAndroidApi.ps1 -EndpointFile $endpoint -Method settings.tab `
  -Arguments '{"value":"sync"}' -Wait
./scripts/Stop-HarnessAndroidApi.ps1 -EndpointFile $endpoint
```

脚本打开已有应用，不安装 APK、不重置配置。`-Package com.harness.portable.syncqa` 用于独立测试包。端点目录必须为空；生成的文件仅当前电脑用户可读，包含令牌，不能提交或分享。停止会话会关闭设备端监听、移除 ADB 转发及端点文件。应用进程结束、Activity 重建也会终止会话，需要重新启动。

## 协议与任务

传输为 UTF-8 JSON，每行一条 JSON-RPC 风格请求，`jsonrpc:"2.0"`、`id`、`auth`、`method` 和对象 `params`。协议版本为 1。输入上限 1 MiB，每个连接最多 64 条请求，空闲超时 15 秒。错误返回 `error.code/message`，不会记录令牌或参数。

查询直接返回状态。修改操作先返回 `operationId`，响应写出后才开始操作。用 `operations.get` 查询 `queued/running/returned/failed`；`-Wait` 自动等待操作返回。连接、下载和同步本身可能继续运行，应再查看相应状态。`events.read` 支持 `after` 序号，保留最近 200 条事件。注册方法取决于当前页面；`api.describe` 返回当前可用操作、描述及参数名。

| 方法 | 用途 |
| --- | --- |
| `ui.tree/find/actions/act` | 原生控件快照、选择、动作目录及语义操作 |
| `app.state/back/show/exit/settings/connections/orientation` | 导航、返回、方向及窗口生命周期 |
| `library.list/create/edit/delete/favorite/open` | 链接库操作；删除和切换保留正常确认流程 |
| `connections.stopAll` | 通过正常应用操作断开连接 |
| `network.status/resolve` | 当前网络与 DNS、主机名解析；不修改连接配置 |
| `sync.status/configure/test/run/cancel` | WebDAV 设置、检测、同步及取消 |
| `settings.tab` | `general/security/sync/about` 四个设置分类 |
| `updates.status/check/apply` | 更新源状态、检测及交给系统安装器 |
| `config.export` | 调用正常导出操作及文件选择回调 |
| `browser.state/reload/back/authenticate/reconnect/disconnect/controls.collapse/evaluate` | 嵌入网页、工具栏及公开 JavaScript 桥接 |
| `dialogs.get/choose`、`files.put/get/list` | 自动化文件选择与会话暂存文件 |

## 控件与敏感信息

`ui.find` 的 `selector` 支持 `automationId`、`dataKey`、临时 `id`、非敏感 `name` 及 `includeHidden`。`ui.act` 使用相同的 `target` 选择器。重复列表项共享稳定 ID，以 `dataKey` 区分；匹配多项返回 `ambiguous`。隐藏、禁用或被模态弹窗阻挡的控件不能操作。未创建的懒加载项需先滚动或 `realize`。

```powershell
./scripts/Invoke-HarnessAndroidApi.ps1 -EndpointFile $endpoint -Method ui.act -Wait `
  -Arguments '{"target":{"automationId":"ConfigurationSync.Address"},"action":"setValue","value":"https://dav.example.test/Harness/"}'
```

支持的动作包括 `invoke`、`longClick`、`setValue`、`setChecked`、`selectItem`、`selectText`、`scroll`、`realize`、`focus`、`setExpanded`、`dismiss`、`setProgress`、`ime` 和 `edit`。每个控件在快照中声明实际支持的动作。滚动参数 `horizontal/vertical` 是相对内容偏移，不是屏幕坐标；`selectText` 使用 `start/length`，`realize` 使用 `index`。新控件和自定义手势必须有稳定 ID、注册操作及行为测试；手势映射见 `spec/android-ui-api-gestures.json`。

复制、剪切和粘贴沿用 Android 的剪贴板权限规则，需等待目标控件所在窗口的 `activeWindow:true`；控件取得文本焦点与窗口取得前台焦点是两件事。

密码、私钥位置、认证令牌输入只写，快照仅报告 `sensitive/hasValue`。切换“显示密码”仍会屏蔽。包含敏感子项的合并节点也屏蔽文本。链接库与浏览器状态去除直连 URL 的用户信息、查询和片段。任务快照和事件不保留输入参数。`browser.evaluate` 是可信自动化的网页脚本能力，脚本及其返回值由调用者负责，不能把它当作网页数据脱敏接口。

`browser.evaluate` 的 `userGesture:true` 用于文件上传等需要用户激活的网页操作。它为单次调用临时启用公开 DevTools 协议，通过本进程的 Android Unix 域套接字调用 `Runtime.evaluate`，不注入屏幕输入，也不开放额外 TCP 端口；调用结束或会话停止后关闭调试。普通脚本使用 WebView 的公开 JavaScript 接口。`raw:true` 返回 JSON 编码后的值。

## 弹窗、导出及上传

Compose 确认框和菜单可通过 `ui.act` 操作或 `app.back` 返回。文件选择在普通模式保留系统界面；API 模式由 `dialogs.get` 返回 `id`、`kind` 和要求，`dialogs.choose` 调用原来的选择结果回调。传入 `cancel:true` 取消；过期 ID 被拒绝。

`files.put` 接收 `name/base64`，单文件最多 512 KiB、每会话最多 100 个文件；文件名保留 Unicode，最多 80 字符且 UTF-8 最多 180 字节，拒绝路径分隔符、控制字符和路径跳转。将返回的 `fileId` 放入 `dialogs.choose.fileIds`，网页收到原文件名。上传文件只写，不能用 `files.get` 读取。创建文件对话框确认后，从 `files.list` 找到生成的导出，再用 `files.get` 取回 Base64。停止会话清理 API 自己创建的缓存。系统权限、系统设置和 APK 安装器属于 Android 系统界面，API 负责交接，不冒充其原生控件。

## 验证

`scripts/test-fixtures/android-automation/coverage.py` 检查交互控件自己的稳定 ID、新控件类型和手势映射。原生 `AndroidAutomationNativeTests` 用真实 TCP 请求验证文本/选择/IME、切换/范围/长按/展开、模态与可见性、懒加载及会话认证。跨进程 `smoke.py` 驱动完整应用、回环 WebDAV 和网页文件选择，拒绝出现没有稳定 ID 的交互节点或未屏蔽的测试凭据。

测试只允许独立模拟器中的 `.syncqa` 包，使用全新配置和回环服务。构建沿用 `scripts/test-fixtures/config-sync/android-qa.init.gradle`；CI 同时保留同步、密钥存储和大小屏设置布局测试，不使用真实服务器账号，也不安装到使用者的正式应用。
