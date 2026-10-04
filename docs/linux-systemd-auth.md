# Linux systemd 自动获取 Web token

Windows、macOS 和 Android 客户端的 SSH 隧道均可选择「systemd 自动（Linux 服务器）」。
服务应运行在 SSH 登录的服务器上；token 获取使用同一套 SSH 身份和主机密钥校验。

## 客户端配置

- 服务名：默认 `dsh-web.service`，也可填写 `dsh-web@4080.service` 等实例服务名。
- 服务范围：默认「用户服务」，对应 SSH 登录用户；已有系统服务选择「系统服务」。
- 日志路径：默认留空，读取该服务本次启动的 journal。
  服务配置 `StandardOutput=append:/…`、`file:/…`、`truncate:/…` 或启动脚本
  自己重定向 stdout 时，在这里填写服务器上的完整日志文件路径。
- 远端端口须与 dsh web 启动 URL 的端口一致。

桌面配置示例（Android 字段为 `auth_mode`、`systemd_unit`、`systemd_scope`、`systemd_log_path`）：

```json
{
  "authMode": "systemd",
  "systemdUnit": "dsh-web.service",
  "systemdScope": "user",
  "systemdLogPath": ""
}
```

## 用户服务示例

在服务器的 `~/.config/systemd/user/dsh-web.service` 保存下面的配置。
将 `ExecStart` 改为实际的 dsh 可执行文件绝对路径，并使用已有部署所需的参数和环境。

```ini
[Unit]
Description=DSH Web

[Service]
Type=simple
ExecStart=%h/.local/bin/dsh web
WorkingDirectory=%h
Restart=on-failure
RestartSec=3
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=default.target
```

在该用户的 SSH 会话中执行：

```sh
systemctl --user daemon-reload
systemctl --user enable --now dsh-web.service
loginctl enable-linger "$USER"
```

`enable-linger` 让用户服务在开机时启动、退出登录后继续运行；按服务器策略可能需要管理员执行。
用户 journal 需要启用持久存储，且当前启动行尚未被日志清理移除。
无法使用用户 journal 时，可预先创建仅该用户可读的日志文件，再将 `StandardOutput`
改为 `append:/完整路径/dsh-web.stdout.log`，并在客户端填写相同日志路径。
系统服务则要求 SSH 用户具有对应 journal 或日志文件的读取权限。

参考：[loginctl](https://github.com/systemd/systemd/blob/main/man/loginctl.xml)、
[journalctl](https://github.com/systemd/systemd/blob/main/man/journalctl.xml)、
[stdout 重定向](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml)。

## 获取与回退

共享脚本 `shared/systemd-auth.sh` 被打包到三端应用，不依赖客户端安装 systemd。
服务必须处于 active 状态且存在 `InvocationID`。journal 按本次启动标识筛选，
仅提取最新的 `dsh web: http…token…` 行；文件日志取最新启动行。
systemd 至少需要支持 `_SYSTEMD_INVOCATION_ID` 字段（233 及以上）。
随后校验端口、用服务器上的 curl 验证 HTTP 303，并再次检查启动标识。
curl 不跟随重定向、不发送 cookie，并绕过环境代理。

脚本只读服务和日志，不执行 sudo、不启动或重启服务。客户端总获取超时为 10 秒。
日志不可读、启动行缺失、token 被拒绝、端口不符或获取期间服务重启时，
回退到凭据库保存的认证输入；没有保存值时，由页面提示粘贴。
获取到的 token URL 不写入配置或诊断日志。

## 验证

```sh
python -m unittest discover -s tests -v
```

该测试在 POSIX sh 下运行真实共享脚本，用模拟的 systemctl、journalctl 和 curl
覆盖服务作用域、启动标识、文件日志、引号路径、错误端口、拒绝 token、日志不可读和重启。
Windows 可使用 Git for Windows 的 sh；也可用 `SYSTEMD_TEST_SHELL` 指定 sh 路径。
