// SPDX-License-Identifier: Apache-2.0
plugins { application }
repositories { mavenCentral() }
dependencies {
    implementation("io.ioka.munarium:munarium-client:1.0.0")
    implementation("com.h2database:h2:2.3.232")
    testImplementation("org.junit.jupiter:junit-jupiter:5.12.2")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}
java { sourceCompatibility = JavaVersion.VERSION_21 }
application { mainClass = "io.ioka.demo.orders.Main" }
dependencyLocking { lockAllConfigurations() }
tasks.register("cacheRuntime") {
    doLast {
        configurations.runtimeClasspath.get().resolve()
        configurations.testRuntimeClasspath.get().resolve()
    }
}
tasks.withType<JavaCompile>().configureEach {
    options.release = 21
    options.encoding = "UTF-8"
    options.compilerArgs.addAll(listOf("-Xlint:all,-serial", "-Werror"))
}
tasks.test {
    dependsOn(tasks.installDist)
    useJUnitPlatform { includeTags(System.getenv("ORDER_TEST_KIND") ?: "unit") }
    outputs.upToDateWhen { false }
    testLogging { events("passed", "failed", "skipped") }
    reports.junitXml.outputLocation = file((System.getenv("ORDER_REPORT_DIR") ?: "build/reports/run") + "/junit")
}
