plugins {
    id("com.android.application")
    id("com.google.protobuf")
    id("kotlin-parcelize")
}

dependencies {
    implementation("androidx.core:core:1.19.0")
    implementation("androidx.annotation:annotation:1.10.0")
    implementation("androidx.appcompat:appcompat:1.8.0")
    implementation("androidx.coordinatorlayout:coordinatorlayout:1.3.0")
    implementation("com.google.android.material:material:1.14.0")
    implementation("com.google.protobuf:protobuf-javalite:4.36.1")

    // CriCri Mods: tests that walk the home screen like a person would (Robolectric, on the PC; also save
    // screenshots of each screen to fusionApp/build/telas for a visual check without a phone).
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("androidx.test:core:1.6.1")
    testImplementation("androidx.test.ext:junit:1.2.1")
}

android {
    namespace = "dev.allofus.fusioncore"
    compileSdk = 37

    buildFeatures {
        prefab = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            all {
                // the PC has little free memory while other builds run
                it.maxHeapSize = "1280m"
                it.systemProperty("robolectric.graphicsMode", "NATIVE")
            }
        }
    }

    defaultConfig {
        minSdk = 24
        targetSdk = 36
        // CriCri Mods: the Play Store package id is permanent once published.
        applicationId = "br.com.cricribr.mods"
        versionCode = 4
        versionName = "0.3.0"
        // Google Play forbids downloading executable code at run time (Device and Network Abuse policy), so a Play
        // build never fetches the unstripped libunity and the Unity base libraries. The CriCri chose (08/11/2026) the
        // site only, no Play Store: gerar-apk-final.ps1 builds with -PcricriPermitirDownloads=true (the CriCri
        // Replay needs those pieces: without them the replay opens paused, no Time.set_timeScale).
        buildConfigField("boolean", "ALLOW_RUNTIME_DOWNLOADS", (project.findProperty("cricriPermitirDownloads") ?: "false").toString())
        // The only game this launcher starts.
        buildConfigField("String", "TARGET_PACKAGE", "\"com.innersloth.spacemafia\"")
        // GPL-3.0: where people get the source of the exact build they have. Must be filled before
        // distributing the app (gerar-apk-final.ps1 refuses to build a release without it).
        buildConfigField("String", "SOURCE_URL", "\"${project.findProperty("cricriSourceUrl") ?: ""}\"")
        // Mods updated from the site without a new APK (ModsRemotos): the signed catalog and the DLLs live here,
        // served as plain files by the CRICRI BR server. https only; "" turns the check off.
        // The CRICRI BR site, for "Conectar conta" (ConectarBr). The test server build passes -PcricriSiteUrl=https://cricribr.com.br:22034
        buildConfigField("String", "SITE_URL", "\"${project.findProperty("cricriSiteUrl") ?: "https://cricribr.com.br"}\"")
        buildConfigField("String", "MODS_URL", "\"${project.findProperty("cricriModsUrl") ?: "https://cricribr.com.br/mods/"}\"")
        ndk {
            abiFilters.add("arm64-v8a")
            // abiFilters.add("armeabi-v7a")
        }
    }

    externalNativeBuild {
        cmake {
            path = File("./src/main/jni/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    signingConfigs {
        create("release") {
            storeFile = file(System.getenv("KEYSTORE_PATH") ?: "keystore.jks")
            storePassword = System.getenv("KEYSTORE_PASSWORD")
            keyAlias = System.getenv("KEY_ALIAS")
            keyPassword = System.getenv("KEY_PASSWORD")
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName("release")
            // we don't need minify tbh
            isMinifyEnabled = false
            //noinspection NotShrinkingResources
            isShrinkResources = false
            proguardFiles("proguard-unity.txt", getDefaultProguardFile("proguard-android-optimize.txt"))
        }
    }

    packaging {
        jniLibs {
            useLegacyPackaging = true
            keepDebugSymbols += listOf("*/armeabi-v7a/*.so", "*/arm64-v8a/*.so")
        }
    }

    lint {
        abortOnError = false
    }
}


protobuf {
    protoc {
        artifact = "com.google.protobuf:protoc:4.35.1"
    }
    generateProtoTasks {
        all().forEach { task ->
            task.builtins {
                create("java") {
                    option("lite")
                }
            }
        }
    }
}