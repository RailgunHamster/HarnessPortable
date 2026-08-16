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
      "localPort": 3080
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
| `directs` | array<string> | 否 | `[]` | 直连 URL，保持添加顺序，去重 |

## 多隧道运行约定（桌面端）

Windows 与 macOS 桌面版允许**同一进程内同时运行多个隧道**：

- 每个 `tunnel.id` 一个独立 SSH session、独立状态与重连循环；
- 每个已连接隧道对应一个工作区标签，可在同一窗口内左右/上下分屏同时显示；
- 同一隧道/直连可通过标签右键“复制标签”打开多个独立 WebView 标签；
- 标签可重命名，自定义 label 作为布局预设的一部分保存；
- 标签右键可四向拆分（复制当前标签到新 pane），也可“切换为”其他隧道/直连；
- 布局预设保存分屏结构 + 每个标签的隧道/直连引用，可多个预设一键切换；
- F11 进入全屏（只留标签和网页），Esc 退出；
- 布局预设包含“管理”标签的位置；
- 应用设置（如关闭主窗口行为）存 `settings.json`，默认“直接退出”；
- 关闭标签不停止隧道，停止隧道才关闭对应标签；
- 直连项可多开：每点击一次“连接”新开一个标签，互不影响；
- 停止/删除某一条隧道不得影响其他隧道；
- 菜单栏/托盘可显示所有活动隧道，并支持“停止全部”；
- 同一 profile 不重复启动（已连接时点击“连接”只聚焦其标签）。

## 主机密钥 TOFU（known_hosts.json）

以 `"host:port"` 为主键保存服务器公钥（Base64 原始字节）与类型。
首次连接记住；相同放行；不同或类型改变则拒绝连接。Android 版历史实现
存于 `SharedPreferences("harness_portable_known_hosts")`，桌面版行为必须对齐。

## 密码凭据

- Windows：DPAPI（CurrentUser）加密后存 `secrets.json`，键为 `tunnel.id`。
- macOS：Keychain，service 建议 `com.harness.portable`，account 为 `tunnel.id`。
- Android：Android Keystore 加密后存 SharedPreferences，键为 `tunnel.id`。

凭据不随 `profiles.json` 导入导出，跨设备迁移时由用户重新输入。
