plugins {
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

java {
    toolchain.languageVersion.set(JavaLanguageVersion.of(25))
}

application {
    mainClass.set("Example")
    applicationDefaultJvmArgs = listOf("--enable-native-access=ALL-UNNAMED", "--illegal-native-access=deny")
}
