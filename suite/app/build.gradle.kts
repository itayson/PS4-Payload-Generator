plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.tayson.ps4linksender"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.tayson.ps4linksender"
        minSdk = 28
        targetSdk = 35
        versionCode = 17
        versionName = "1.5.0"
    }

    flavorDimensions += "abi"
    productFlavors {
        create("armv7") {
            dimension = "abi"
            versionNameSuffix = "-armv7"
        }
        create("arm64") {
            dimension = "abi"
            versionNameSuffix = "-arm64"
        }
        create("universal") {
            dimension = "abi"
        }
    }

    buildTypes {
        debug {
            applicationIdSuffix = ".payloadlab"
            versionNameSuffix = "-experimental"
        }
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    testImplementation("junit:junit:4.13.2")
}
