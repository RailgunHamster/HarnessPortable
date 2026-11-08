pluginManagement {
    repositories {
        // The upstream compiler JAR redirects to GitHub Releases, which can time out on CI.
        // Limit the mirror to this module; resolve other CI dependencies from upstream.
        if (System.getenv("GITHUB_ACTIONS") == "true") {
            maven("https://maven.aliyun.com/repository/public") {
                content { includeModule("org.jetbrains.kotlin", "kotlin-compiler-embeddable") }
            }
        }
        if (System.getenv("GITHUB_ACTIONS") != "true") {
            maven("https://maven.aliyun.com/repository/gradle-plugin")
            maven("https://maven.aliyun.com/repository/google")
            maven("https://maven.aliyun.com/repository/public")
        }
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        if (System.getenv("GITHUB_ACTIONS") == "true") {
            maven("https://maven.aliyun.com/repository/public") {
                content { includeModule("org.jetbrains.kotlin", "kotlin-compiler-embeddable") }
            }
        }
        if (System.getenv("GITHUB_ACTIONS") != "true") {
            maven("https://maven.aliyun.com/repository/google")
            maven("https://maven.aliyun.com/repository/public")
        }
        google()
        mavenCentral()
    }
}
rootProject.name = "harness-portable"
include(":app")
