# Windows UI 自动化 API（协议 1）

本接口覆盖 Windows 应用自身的控件操作、连接库、标签、分屏、布局、设置、认证提示、浏览器和托盘。普通控件由运行时自动发现；自定义拖拽和应用操作有独立的语义方法。UI 与 API 调用同一个操作实现，不查找并反射调用私有事件处理器，也不依赖屏幕坐标。

接口属于按需启动的开发/测试能力。普通启动不会监听接口。当前传输是仅允许当前 Windows 用户连接的命名管道；不开放 TCP 端口。Android 和 macOS 尚无此运行时接口。

## 启动隔离实例

使用 PowerShell 7 和 .NET 10 开发构建。启动脚本为子进程创建独立配置，不修改当前终端的环境变量：

```powershell
dotnet build windows/HarnessPortable.Windows/HarnessPortable.Windows.csproj -c Release
$api = ./scripts/Start-HarnessApi.ps1 -Offscreen
$endpoint = $api.EndpointFile
```

`--automation-api` 后必须是新的绝对路径。文件原子写入，包含协议、进程 ID 和随机管道名；已有文件不会覆盖。正常退出会删除本实例的描述文件。测试可额外传 `--automation-offscreen`，让初始主窗口在屏幕外显示且不主动激活。它不会安装应用或改动正式配置。API 模式不自动检查外部更新源；测试可显式调用 `updates.check`。

已有正式实例不受上述环境变量影响。测试应始终使用新数据目录，避免读写个人配置和真实连接。

## 调用与发现

在同一 PowerShell 会话调用；也可把描述文件的绝对路径复制到另一终端：

```powershell
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method api.describe
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method ui.actions
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method ui.tree
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method ui.find `
  -Arguments '{"selector":{"automationId":"MainWindow.SidebarToggleButton"}}'
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method ui.act -Wait `
  -Arguments '{"target":{"automationId":"MainWindow.SidebarToggleButton"},"action":"invoke"}'
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method app.exit
```

`api.describe` 是当前实例的权威方法清单，包含说明、允许的参数、必填项、输入 schema 和是否修改状态。`ui.actions` 提供控件动作及各动作必需的参数。一个具体 UI 操作由 **方法 + 目标 + 动作 + 参数** 唯一表达，不需要为每个按钮维护另一个容易过期的清单。

`ui.tree` 返回当前控件的 ID、父节点、所属窗口/标签、显隐、启用状态、可用动作和状态属性。`includeHidden:true` 可查看当前已创建的隐藏页面。`ui.find` 支持 `id`、`automationId`、`elementName`、`type`、`name`、`dataKey`、`tabId`、`windowId`。

- `automationId` 是源码中的稳定标识，不随可见文字翻译而改变。
- `id` 是该控件在当前实例内的身份。控件销毁后它会失效，不能跨进程重用。
- 列表/模板可重复使用同一个 `automationId`。用 `dataKey`/`tabId`/`windowId` 筛选，或从树中取得具体 `id`。模糊的目标会返回 `ambiguous`，不会随意选第一个。
- 虚拟列表先对选择器调用 `items`/`realize`，再查询已实现的行控件。

## 控件动作

| 动作 | 额外参数 | 对应行为 |
|---|---|---|
| `invoke` | 无 | 调用真实控件的 Invoke provider，包含绑定的 Command |
| `setValue` | `value`，可选 `commit` | 文本、密码、可编辑下拉框或数值范围；保留绑定，文本默认提交数据源 |
| `setChecked` | `value` | 复选/切换状态；三态控件允许 `null` |
| `select` / `realize` | `index` | 选择或实现列表/下拉条目 |
| `selectItem` | 可选 `mode`: replace/add/remove | 调用条目的实际选择 provider |
| `items` | 无 | 查询选择器条目及选中位置 |
| `selectText` | `start`、`length` | 选择文本范围 |
| `replaceSelection` | `value`，可选 `commit` | 走真实编辑与撤销路径替换所选文本 |
| `edit` | `value`，可选 `commit` | copy/cut/paste/undo/redo/selectAll/delete；执行实际路由命令 |
| `setExpanded` | 布尔 `value` | 展开/收起菜单、下拉框或分组 |
| `scroll` | `vertical` / `horizontal` | 设置滚动偏移 |
| `contextMenu` / `dismiss` | 无 | 打开真实上下文菜单或关闭菜单 |
| `focus` | 无 | 将焦点放到控件 |
| `key` | `value`，如 Enter/Escape/F11 | 经过应用的 WPF 键盘事件路径，不注入系统键盘输入 |
| `window` | `value` | show/hide/activate/close/normal/maximized/minimized/resize；resize 可带 widthPixels/heightPixels |

只应使用目标节点实际报告的动作。隐藏、禁用、只读、越界、已销毁的控件会失败。调用 `setValue` 时可用 `commit:false` 保留编辑中状态，以测试失焦提交。密码以及标记为敏感的认证输入只写不回显，树中只报告是否有值；接口不读取密码库或私钥内容。

## 应用操作

以 `api.describe` 的签名为准。主要分区：

| 分区 | 方法 |
|---|---|
| 应用/状态 | `app.state`、`app.show`、`app.settings`、`app.fullscreen`、`app.escape`、`app.exit` |
| 连接库 | `library.list/create/open/edit/delete/favorite/group/move/addMenu`、`connections.stopAll` |
| 标签 | `workspace.tab.select/close/rename/duplicate/split/move/switch` |
| 窗格 | `workspace.pane.maximize/restore/resize` |
| 布局 | `workspace.layouts`、`workspace.layout.save/load/delete` |
| 浏览器 | `browser.state/reload/authenticate/reconnect/disconnect/evaluate/capture/cdp` |
| 更新 | `updates.check/apply` |
| 托盘 | 从 `TrayIconService.Actions` 自动注册，例如 `tray.open/settings/update/exit` |

应用状态提供当前窗格、标签、连接和更新状态。直接网页的连接 key 是 URL 的 SHA-256 标识；展示地址去掉凭据、查询和 fragment。不要自行拼接连接 key，使用 `library.list` 返回的值。

`browser.state` 提供 `ready`、`navigating`、`navigationSucceeded`、`parked` 和网页提示是否打开。初始化尚未完成时浏览器命令返回 `unavailable`。`browser.evaluate` 调用现有 WebView2，参数为 `tabId`、`script`，可用 `raw:true` 返回原始 JSON 字符串。`browser.cdp` 接受 `tabId`、`command`、可选对象 `parameters`，通过 WebView2 的公开 DevTools API 操作嵌入页面，返回原始 JSON 字符串；`capture` 返回 PNG 的 Base64。

设置、编辑器和认证框的各字段可直接通过控件 API 操作，不额外维护一套验证规则。API 模式把 OS 原生消息框和文件选择框接到应用拥有的可操作提示：例如 `dialog.choice.yes/no`、`dialog.file.path/accept/cancel`。文件存在、目录有效和覆盖确认仍执行；普通启动继续使用原生 Windows 对话框。操作系统安装器、文件浏览器自身的内部 UI 不属于本应用接口。

## 异步、弹窗和断言

修改操作返回 `operationId`。响应已写入管道后才开始 UI 操作，因此退出命令也能先收到响应。`operations.get` 查询 queued/running/returned/failed；PowerShell `-Wait` 等待处理器返回。

**处理器返回不等于 SSH 已连接或网页已加载。** 对连接检查 `library.list`/`app.state`，对网页检查 `browser.state`，对界面用 `ui.wait`。例如：

```powershell
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method ui.wait `
  -Arguments '{"selector":{"automationId":"SettingsWindow.Window"},"timeoutMs":5000}'
./scripts/Invoke-HarnessApi.ps1 -EndpointFile $endpoint -Method ui.wait `
  -Arguments '{"selector":{"automationId":"SettingsWindow.SourceAddressBox"},"property":"properties.value","equals":"C:\\fixture\\updates","timeoutMs":5000}'
```

打开模态编辑器的操作可能保持 running，直到保存或取消。在另一条 API 请求中操作编辑器即可；不要先 `-Wait` 阻塞等待尚未回答的编辑器。网页 prompt/confirm 同样可在浏览器命令运行时，通过另一连接回答。等待具有 1–30000 ms 的上限，并支持调用方取消；不阻塞 UI 线程。

`events.read` / `events.wait` 提供操作与连接、配置、更新变化的事件游标。先记录 cursor，再发操作，可在之后读回已观察到的变化；`events.wait` 可以挂起至新变化到达。事件不记录输入参数和密码，最多保留 1024 条；历史已超出缓冲区时明确返回 `truncated:true`，调用方应重读完整状态。批量读取返回最后一条已交付事件的 cursor，不跳过未读事件。

失败使用稳定的代码，例如 `invalid_request`、`invalid_arguments`、`unknown_method`、`not_found`、`ambiguous`、`disabled`、`unsupported`、`unavailable`、`conflict`、`timeout`。后台操作失败还报告异常类型和方法栈，不回显请求参数或第三方异常消息。

## 传输和资源边界

每个命名管道连接处理一个 UTF-8 JSON 行并返回一个 JSON 行：

```json
{"version":1,"id":"test-1","method":"ui.tree","params":{}}
```

响应包含 `version`、原 `id` 和 `result` 或 `error`。请求 ID 必须是 1–128 个字符的字符串。未知参数、缺失必填参数、错误类型会被拒绝。参数缺省时可省略 `params`。请求最多 262144 个字符、JSON 深度 32；超大或不完整帧会关闭连接。

最多同时服务 16 条连接、64 个未结束 UI 操作。最近完成的任务最多保留 256 个，结果总计最多 64 MiB，单个结果最多 16 MiB。已过期任务返回 `not_found`；需要长期保留的结果由调用方保存。描述文件没有认证令牌，权限由命名管道的 `CurrentUserOnly` 强制执行。

## 新 UI 的覆盖门禁

`AutomationCoverageTests` 和 `.github/workflows/windows-api.yml` 在提交/PR 时检查：

1. 交互控件具有显式稳定 ID；新的原生输入控件类型须评审适配。
2. 自定义鼠标/拖拽事件必须在 `spec/windows-ui-api-gestures.json` 映射到已注册的方法。
3. 动态按钮/菜单必须通过 `AutomationControls` 创建，托盘从统一 action 列表生成。
4. 原生消息框/文件框不能绕过自动化对话框适配器。
5. 协议、IPC、控件行为和真实产品的跨进程流程必须通过。

门禁验证接口覆盖，端到端回归验证行为；新增分支仍需要相应正反例，接口清单不能代替场景测试。新增复杂控件时要增加适配和行为测试，再将类型加入支持集合。

```powershell
dotnet test windows/HarnessPortable.Windows.Tests/HarnessPortable.Windows.Tests.csproj -c Release
dotnet run --project scripts/test-fixtures/windows-automation/Smoke.csproj -c Release -- `
  windows/HarnessPortable.Windows/bin/Release/net10.0-windows/HarnessPortable.exe `
  artifacts/automation-run-new
```

输出目录必须不存在或为空。回归运行实际产品进程，通过公开 API 驱动，不调用私有 UI 方法；使用随机端口的本地网页、隔离配置和屏幕外窗口，结束后关闭自身子进程。
