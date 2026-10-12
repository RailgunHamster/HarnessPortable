# Harness Portable 配置约定（跨平台数据契约）

Windows / macOS / Android 各自实现存储与 UI，但**配置字段语义保持一致**，
以便未来导入导出或迁移。

## profiles.json

位置：

- Windows：`%APPDATA%\HarnessPortable\profiles.json`
- macOS：`~/Library/Application Support/HarnessPortable/profiles.json`
- Android：`SharedPreferences("harness_portable")` 中的 `tunnels` / `direct_urls`
  两个 JSON 字符串（历史兼容，暂不迁移）

结构：

```json
{
  "version": 1,
  "tunnels": [
    {
      "id": "uuid",
      "name": "可选名称",
      "sshHost": "服务器地址（IP / 域名 / NetBIOS 名）",
      "sshPort": 22,
      "user": "用户名",
      "remoteHost": "127.0.0.1",
      "remotePort": 3080,
      "localPort": 3080,
      "authMode": "nssm",
      "identityFile": ""
    }
  ],
  "directs": [
    "http://192.168.0.104:4096"
  ]
}
```

字段规则：

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `version` | int | 否 | 1 | 契约版本，升级时递增 |
| `tunnels` | array | 否 | `[]` | 隧道列表 |
| `tunnels[].id` | string | 是 | uuid | 稳定标识，密码凭据关联它 |
| `tunnels[].name` | string | 否 | 空 | 为空时 UI 显示 `sshHost` |
| `tunnels[].sshHost` | string | 是 | — | 裸名/IP/域名 |
| `tunnels[].sshPort` | int | 否 | 22 | 1–65535 |
| `tunnels[].user` | string | 是 | — | SSH 用户名 |
| `tunnels[].remoteHost` | string | 否 | 127.0.0.1 | 服务器侧目标 |
| `tunnels[].remotePort` | int | 否 | 3080 | 1–65535 |
| `tunnels[].localPort` | int | 否 | 3080 | 本地监听端口；被占用时依次 +1 到 +9 |
| `tunnels[].authMode` | string | 否 | `nssm` | Web 登录方式：`nssm`（通过 SSH 读取 Windows NSSM 日志）/ `launchd`（macOS 客户端通过 SSH 读取 Mac Launch Agent 日志）/ `systemd`（三端通过 SSH 读取 Linux systemd 服务的本次启动 journal 或指定文件日志）/ `manual`（使用凭据库保存的认证输入，缺失时页面提示粘贴）。自动获取失败回退到保存的输入，再退到页面粘贴框。Android 键名 `auth_mode` |
| `tunnels[].identityFile` | string | 否 | 空 | SSH 私钥路径。为空时桌面端尝试 `~/.ssh/id_ed25519`、`id_ecdsa`、`id_rsa`（macOS 还会用 ssh-agent）。Android 端键名为 `identity_file`，通常是应用私有目录里导入的副本。密钥登录可不填密码。 |
| `tunnels[].systemdUnit` | string | 否 | `dsh-web.service` | `authMode=systemd` 使用的 Linux 服务名，可填写实例服务（如 `dsh-web@4080.service`）；Android 键名 `systemd_unit` |
| `tunnels[].systemdScope` | string | 否 | `user` | `user` 为 SSH 登录用户的 systemd 用户服务，`system` 为系统服务；Android 键名 `systemd_scope` |
| `tunnels[].systemdLogPath` | string | 否 | 空 | 可选的服务器完整日志路径；空时按本次 `InvocationID` 读取 journal；文件重定向部署填写 stdout 日志路径。Android 键名 `systemd_log_path` |
| `directs` | array<string> | 否 | `[]` | 直连 URL，保持添加顺序，去重 |

## 直连自动认证（按端存储）

直连没有 SSH 一跳，隧道 `authMode` 不适用；如需自动登录，各端把**按 URL** 的直连认证模式
存在本机 `settings.json`（不改跨平台 `directs` 契约）：

- macOS：`directAuthModes`（`none` / `launchd`），读取本机
  `/tmp/dsh-web.stdout.log`（`com.deepseek.dsh-web` Launch Agent）；
- Windows：`DirectAuthModes`（`none` / `nssm`），在本机执行与隧道 NSSM 模式相同的
  发现脚本（定位 NSSM 托管的 dsh 服务、读 stdout 日志、curl 验证 303），端口一致才把
  token query 嫁接到直连地址。

默认全部 `none`（行为不变）；日志缺失、token 失效或端口不一致时地址原样打开，
沿用页面 401 粘贴框兜底。

## systemd 自动认证

三端还支持 `tunnels[].authMode=systemd`（Android 为 `auth_mode=systemd`）：
经 SSH 读取 Linux 服务的启动 token URL，检查远端端口，在服务器侧以无 cookie、
不跟随跳转的 curl 请求验证 HTTP 303，并确认服务启动标识没有变化。
默认不改变旧配置的 `nssm` 模式。失败时回退到保存的 Web 认证输入，再退到页面粘贴框，
不得中断已建立的 SSH 隧道。部署说明见 [Linux systemd 自动认证](../docs/linux-systemd-auth.md)。

## 多隧道运行约定（桌面端）

Windows 与 macOS 桌面版允许**同一进程内同时运行多个隧道**：

- 每个 `tunnel.id` 一个独立 SSH session、独立状态与重连循环；
- 每个已连接隧道对应一个工作区标签，可在同一窗口内左右/上下分屏同时显示；
- 同一隧道/直连可通过标签右键“复制标签”打开多个独立 WebView 标签；
- 标签可重命名，自定义 label 作为布局预设的一部分保存；
- 标签右键可四向拆分（复制当前标签到新 pane），也可“切换为”其他隧道/直连；
- 布局预设保存分屏结构、每个标签的隧道/直连引用，以及拖动后的 pane 尺寸
  （DockWidth/DockHeight 的 GridLength 值），可多个预设一键切换；
- F11 进入全屏（只留标签和网页），Esc 退出；
- 连接库是主窗口可折叠侧栏；应用设置与更新使用**独立设置窗口**，工作区只放会话标签。
  托盘「管理 / 设置」打开设置窗口，设置中可返回连接库。
  旧预设里残留的 `Kind = "management"` 标签在恢复时被忽略（不报错、不再重建该标签）。
- 三端界面可选择系统、浅色或深色，颜色/圆角/字体取自 DSH 前端设计系统
  （`dsh-client-ui-theme` 的 token），不再各自硬编码；技术值（host、端口、URL、版本）用等宽字体。
- 应用设置（如关闭主窗口行为、启动恢复上次布局、更新源）存 `settings.json`，
  默认“直接退出”、默认恢复上次工作区，默认不自动重连 SSH；
- 桌面 `workspace.json` 保存自动快照，`layouts.json` 仍保存手动命名的预设。
  标签可带 `Selected` / `Active`（Windows）或 `selected` / `active`（macOS），旧配置缺失时按首个标签恢复。
  连接库偏好以 `tunnel:<id>` / `direct:<url>` 为键，包含名称、常用、分组与最近打开时间，保存在桌面应用设置中。
- 更新源是**两个槽位加一个选择**，两个地址都会保留，切换不丢：
  - Windows：`UpdateHomeUrl`（局域网共享，默认
    `\\server-home\public\Software\HarnessPortable-Releases`）与
    `UpdateGitHubUrl`（默认仓库地址），`UpdateSourceSelected` 取 `home` / `github`
    决定实际用哪个；新安装默认选 `github`。
  - Android：`update_source_home` / `update_source_github` /
    `update_source_selected`，默认选 `github`（手机读不了 UNC）。
  - macOS：`updateHomeUrl` / `updateGithubUrl` / `updateSourceSelected`，默认选
    `github`；home 默认值 `smb://server-home/public/Software/HarnessPortable-Releases`，
    运行时映射到 `/Volumes/public[…]` 挂载点（`macos.json` feed，与 android.json
    同族，多一个 `sha256` 校验字段）；github 槽位默认
    `https://github.com/RailgunHamster/HarnessPortable/releases/latest/download`。
  - 两者都存在时旧字段 `UpdateServerUrl` / `update_server_url` 只作镜像，
    不再决定行为；首次升级它会被折进对应的槽位，不丢用户原本填的地址；
- 更新源草稿不会自动落盘，明确保存后生效；已有的槽位选择及旧字段迁移保留用户原本使用的源。
- 关闭标签不停止隧道，停止隧道保留对应标签，删除配置才关闭对应标签；
- 直连重复打开聚焦已有标签，通过“复制标签”可打开独立网页；
- 停止/删除某一条隧道不得影响其他隧道；
- 菜单栏/托盘可显示所有活动隧道，并支持“停止全部”；
- 同一 profile 不重复启动（已连接时点击“连接”只聚焦其标签）。

## 手机运行约定

- 单窗口、单服务，没有桌面式标签和分屏。
- 返回连接列表、进入设置保留当前隧道及同一个 WebView，隐藏网页时暂停它。
- 已有服务时打开另一服务需要确认；取消保留旧服务，确认后停止旧服务并释放旧 WebView。
- 主动断开或删除当前连接停止服务、释放网页。开启浏览器全屏只影响服务页，连接列表和设置保留系统导航。

## 主机密钥 TOFU（known_hosts.json）

以 `"host:port"` 为主键保存服务器公钥（Base64 原始字节）与类型。
首次连接记住；相同放行；不同或类型改变则拒绝连接。Android 版历史实现
存于 `SharedPreferences("harness_portable_known_hosts")`，桌面版行为必须对齐。

## 密码凭据

密码是可选的：有可用私钥（`identityFile` 或桌面默认身份文件）时不必保存密码。

- Windows：DPAPI（CurrentUser）加密后存 `secrets.json`，键为 `tunnel.id`。
- macOS：Keychain，service 建议 `com.harness.portable`，account 为 `tunnel.id`。
- Android：Android Keystore 加密后存 SharedPreferences，键为 `tunnel.id`。

凭据不随 `profiles.json` 导入导出，跨设备迁移时由用户重新输入。

SSH 认证顺序：公钥优先，其次才是已保存的密码。每个连接最多提交一次密码；认证失败（含错误密码、Permission denied、Too many authentication failures）直接进入失败状态，**不得**进入断线重连循环，以免远程 sshd 因反复失败拒绝访问。断线重连只适用于曾经连接成功之后的网络中断。

## Web 认证输入（可选）

每个隧道可以额外保存一条可选的 Web 登录信息，用于 dsh web 的
`?token=…` 握手。它在编辑界面（选择“手动输入”时）填写，**不是 `profiles.json` 字段**，
也不随配置导入导出。

- 接受的形式：服务器上 `dsh web` 打印的完整 URL（`http://host:port/?token=…`）、
  裸查询（`?token=…`）、`key=value`（`token=…`），或 token 本身
  （dsh 的启动 token 是 base64url，如 `x1Yz…_-`）。
- 存储位置（键由 `tunnel.id` 派生）：
  - Windows：DPAPI 加密后存 `secrets.json`，键 `<tunnel.id>#webauth`；
  - macOS：Keychain，service `com.harness.portable`，account `<tunnel.id>#webauth`；
  - Android：Android Keystore 加密后存 SharedPreferences，键 `web_<tunnel.id>`。
- 使用时机：`authMode=manual` 时每次连接都用它；`authMode=nssm` / `launchd` / `systemd` 时仅在服务器侧
  抓取失败后回退到它，两者都没有时打开裸地址，由页面的 401 触发粘贴框。
- 归一化：非 URL 的裸 token 拼成 `http://<remoteHost>:<remotePort>/?token=<token>`，
  随后按实际本地转发端口重写（token 不带本地端口信息）。
  token 已失效而 cookie 仍有效时服务端回 303 到干净的 `/`，因此保存旧 token 无害。
- 删除隧道时一并删除该条凭据；编辑界面清空字段即删除。
