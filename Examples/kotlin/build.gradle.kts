plugins {
    kotlin("jvm") version "2.4.0"
    application
}

repositories {
    if (providers.gradleProperty("vesselLocal").isPresent) {
        mavenLocal {
            content { includeModule("io.github.noyzone99", "vessel-storage") }
        }
    }
    mavenCentral()
}

val vesselVersion = providers.gradleProperty("vesselVersion").getOrElse("0.0.1-alpha.1")

dependencies {
    implementation("io.github.noyzone99:vessel-storage:$vesselVersion")
}

kotlin {
    jvmToolchain(25)
}

application {
    mainClass.set("ExampleKt")
    applicationDefaultJvmArgs = listOf("--enable-native-access=ALL-UNNAMED", "--illegal-native-access=deny")
}
