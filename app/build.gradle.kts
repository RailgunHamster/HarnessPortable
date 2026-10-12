import java.util.Properties
import java.io.FileInputStream

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

val keystoreProperties = Properties().apply {
    val f = rootProject.file("keystore.properties")
    if (f.exists()) load(FileInputStream(f))
}
val signingFields = listOf("storeFile", "storePassword", "keyAlias", "keyPassword")
val hasReleaseSigning = signingFields.all { !keystoreProperties.getProperty(it).isNullOrBlank() }
    && rootProject.file(keystoreProperties.getProperty("storeFile")).isFile

gradle.taskGraph.whenReady {
    if (!hasReleaseSigning && allTasks.any { it.project == project && it.name in listOf("packageRelease", "signReleaseBundle") }) {
        throw GradleException("Release 签名配置缺失：请提供 keystore.properties（storeFile、storePassword、keyAlias、keyPassword）和有效的 keystore 文件。Debug 构建无需这些文件。")
    }
}

android {
    namespace = "com.harness.portable"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.harness.portable"
        minSdk = 24
        targetSdk = 36
        versionCode = 20
        versionName = rootProject.file("VERSION").takeIf { it.exists() }?.readText()?.trim() ?: "2.5.0"
    }

    signingConfigs {
        if (hasReleaseSigning) create("release") {
            storeFile = rootProject.file(keystoreProperties["storeFile"] as String)
            storePassword = keystoreProperties["storePassword"] as String
            keyAlias = keystoreProperties["keyAlias"] as String
            keyPassword = keystoreProperties["keyPassword"] as String
        }
    }

    buildTypes {
        release {
            signingConfig = if (hasReleaseSigning) signingConfigs.getByName("release") else null
            isMinifyEnabled = false
            isShrinkResources = false
            isDebuggable = false
        }
    }

    buildFeatures {
        compose = true
    }
    sourceSets.getByName("main").assets.srcDir(rootProject.file("shared"))

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }

    kotlinOptions {
        jvmTarget = "11"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    // Actively maintained JSch fork: modern OpenSSH kex + signature algorithms
    implementation("com.github.mwiede:jsch:0.2.21")
}
