plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Same version as the Windows app (set by the build: -PappVersion=1.0.N -PappCode=N).
val appVersion = (project.findProperty("appVersion") as String?) ?: "1.0.0"
val appCode = ((project.findProperty("appCode") as String?) ?: "1").toInt()

android {
    namespace = "com.ibrata2.ibradm"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.ibrata2.ibradm"
        minSdk = 29
        targetSdk = 35
        versionCode = appCode
        versionName = appVersion
    }

    signingConfigs {
        create("release") {
            val ks = System.getenv("ANDROID_KEYSTORE_FILE")
            if (ks != null && file(ks).exists()) {
                storeFile = file(ks)
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (System.getenv("ANDROID_KEYSTORE_FILE") != null) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    // One APK per processor type: the download engine (Python + yt-dlp + FFmpeg) is big for each.
    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a", "armeabi-v7a", "x86_64")
            isUniversalApk = false
        }
    }

    packaging {
        jniLibs { useLegacyPackaging = true }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { buildConfig = true }
}

val ytdl = "0.18.1"
dependencies {
    implementation("io.github.junkfood02.youtubedl-android:library:$ytdl")
    implementation("io.github.junkfood02.youtubedl-android:ffmpeg:$ytdl")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
}
