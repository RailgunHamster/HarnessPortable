# WebDAV 配置同步

Windows、macOS 和 Android 共用版本 1 的同步格式。在自己的 WebDAV 服务中创建私有目录，再在每台设备分别填写该目录、用户名和密码。Windows / macOS 的入口位于“设置 → 数据与安全”，Android 位于“设置 → WebDAV 同步”。

保存设置后先点“测试目录”，再点“立即同步”。目录测试只验证访问；首次同步的条件写入会验证写权限。自动同步默认关闭，开启后在启动、回到前台、配置变更后及前台每 5 分钟执行。修改地址草稿不会改变已保存的同步目标。

## 内容

| 同步内容 | 处理方式 |
|---|---|
| 链接库 | 保存的 SSH 连接与直连网页，包含服务器、端口、账号名和登录模式 |
| 链接库偏好 | 收藏、分组、显示名称及顺序；Android 会保留并展示桌面端的名称、分组和排序 |
| 外观 | 三端共享 system / light / dark |
| 平台偏好 | Windows / macOS 的关闭行为、恢复与重连、侧栏状态；Android 的方向与服务全屏；分别使用平台命名空间 |
| 密码与信任 | 不上传 SSH 密码、私钥及路径、网页登录令牌、Cookie 或主机密钥信任记录 |
| 本机配置 | WebDAV 账号、更新源与代理、窗口位置、工作区快照和正在运行的会话保留在本机 |

直连 URL 中的账号信息、查询参数及片段不上传。原设备保留原 URL，新设备收到不带登录参数的地址。去除参数后地址重复时会停止同步并提示，避免合并不同的本机链接。

新设备收到的连接没有密码，需要在本机补填。已有相同 ID、SSH 主机、端口和账号的连接保留本机密码及私钥路径；身份改变后不会继续使用旧服务器的凭据。配置变更在下次连接生效，不重建当前网页或自动建立新隧道。

## 合并与错误

固定文件名为 `harness-settings-v1.json`，只写这一份文件，不创建或删除目录中的其他文件。文件不加密，但内容采用严格白名单，地址、账号名等连接元数据仍应放在私有目录中。HTTPS 默认启用，明文 HTTP 必须明确允许。重定向会失败，不向重定向目标发送凭据。

首次按连接 ID 合并，两端独立添加的同名连接分别保留。后续通过上次成功同步的基线进行三方比较：独立修改自动合并，删除可传播到未修改的另一端；同一项目两端都修改、或删除与编辑冲突时，需要选择冲突项保留本机还是云端。其他无冲突项目仍合并。确认前若本机或云端再次改变，需要重新同步并确认。

新文件使用 `If-None-Match: *` 创建；替换已有文件要求强 ETag，并发送 `If-Match`。412 会重新读取，最多尝试三次；没有强 ETag 时停止上传。格式未知、含敏感或未知字段、损坏或超过 1 MiB 的文档均不应用到本机。同步期间的本机修改也不会被旧快照覆盖。

Android 在一次 SharedPreferences 提交中保存链接库、偏好及同步基线。Windows / macOS 写入本机恢复日志，启动时恢复未完成的配置提交。身份变化清除的旧凭据不会从云端恢复。取消后本机配置不替换；若上传已完成，下次同步会重新检查云端。

## Windows 自动化

界面和 API 共用应用操作，密码只写不读：

```json
{"method":"sync.configure","params":{"url":"https://nas.example/dav/Harness/","username":"user","password":"local-only","autoSync":false,"allowHttp":false}}
{"method":"sync.test"}
{"method":"sync.run"}
{"method":"sync.status"}
{"method":"sync.run","params":{"choice":"remote"}}
{"method":"sync.cancel"}
```

省略 `password` 保留同一地址与账号的已存密码；`clearPassword:true` 清除当前目标的本机密码。更换目标或账号后需重填密码或明确清除。`sync.status` 仅报告 `hasPassword`；事件不包含密码。API 的任务和等待规则见 [Windows 自动化协议](windows-automation-api.md) 和 [Android 原生适配协议](android-automation-api.md)。macOS 尚未提供运行时自动化 API。

## 验证

三端使用同一份 `spec/fixtures/shared-configuration-v1.json`。测试使用临时目录、随机回环端口和虚构凭据；不接入用户的真实 WebDAV。

```powershell
dotnet test windows/HarnessPortable.Windows.Tests/HarnessPortable.Windows.Tests.csproj -c Release
dotnet run --project scripts/test-fixtures/windows-automation/Smoke.csproj -c Release -- windows/HarnessPortable.Windows/bin/Release/net10.0-windows/HarnessPortable.exe artifacts/sync-api-new
./gradlew.bat :app:testDebugUnitTest
./gradlew.bat --init-script scripts/test-fixtures/config-sync/android-qa.init.gradle :app:assembleDebug :app:assembleDebugAndroidTest
```

原生 Android 专项为 `com.harness.portable.ConfigurationSyncNativeTests`，只在自有的独立模拟器中安装 `.syncqa` 包及测试包后运行。macOS CI 使用隔离数据目录，运行共享格式、合并规则和真实回环 HTTP / Keychain 适配测试。
