# Android 版构建与发布

## 环境要求

- JDK 17 或 21（AGP 8.7.3 / Gradle 8.11.1；实测 21 可用）
- Android SDK（`compileSdk 36`、`minSdk 24`、`targetSdk 36`）
- 首次构建会自动下载 Gradle 8.11.1

> ⚠️ **JAVA_HOME 失效是最常见的构建失败原因。**
> Gradle 与 `apksigner` 都读 `JAVA_HOME`，如果它指向一个已被卸载的 JDK，
> 构建会直接失败并只报一句
> `ERROR: JAVA_HOME is set to an invalid directory: ...`。
> 报错信息不会告诉你去哪找可用的 JDK，所以先确认这个路径真实存在：
>
> ```powershell
> Test-Path $env:JAVA_HOME
> # 不存在的话，找一下实际装了哪个 JDK：
> Get-ChildItem "C:\Program Files\Microsoft" -Directory |
>   Where-Object { Test-Path (Join-Path $_.FullName "bin\java.exe") }
> # 然后只为当前会话指定（不用改系统环境变量）：
> $env:JAVA_HOME = "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot"
> ```
>
> 会话级赋值在本仓库内足够：`.\gradlew.bat` 与
> `build-tools\<版本>\apksigner.bat` 都会继承它。

SDK 路径二选一：

```text
# 方式 1：仓库根目录 local.properties（已被 gitignore，不提交）
sdk.dir=C:\\Users\\你的用户名\\AppData\\Local\\Android\\Sdk

# 方式 2：环境变量
ANDROID_HOME=C:\Users\你的用户名\AppData\Local\Android\Sdk
```

## 构建 Debug APK

```powershell
.\gradlew.bat :app:assembleDebug
```

产物：

```text
app/build/outputs/apk/debug/app-debug.apk
```

## 构建 Release APK（签名）

Release 使用 `keystore.properties` 指定的签名，仓库根目录需要存在：

```text
keystore.properties        # storeFile / storePassword / keyAlias / keyPassword
release.keystore           # 实际签名文件（不要提交到 git）
```

`keystore.properties` 示例：

```properties
storeFile=release.keystore
storePassword=你的密码
keyAlias=你的别名
keyPassword=你的密码
```

执行：

```powershell
.\gradlew.bat :app:assembleRelease
```

产物：

```text
app/build/outputs/apk/release/app-release.apk
```

## 其他常用命令

```powershell
# 清理
.\gradlew.bat clean

# 重新生成全部图标（修改 branding 后必须先执行）
cd branding
npm install
npm run generate
cd ..

# Android Studio 手动打包
# Build → Generate Signed Bundle / APK...
```

## 版本号位置

编辑：

```text
app/build.gradle.kts
```

```kotlin
versionCode = 11
versionName = "2.4"
```

## 构建流程（完整）

1. 修改代码或图标；
2. 如改了图标：`cd branding && npm install && npm run generate && cd ..`
3. Debug 验证：`.\gradlew.bat :app:assembleDebug`
4. Release 打包：`.\gradlew.bat :app:assembleRelease`
5. 安装验证后提交。

## 当前签名配置说明

`app/build.gradle.kts` 从根目录 `keystore.properties` 读取四个字段，不要把这个
文件和 `.keystore`/`.jks` 提交到仓库（`.gitignore` 已排除）。
