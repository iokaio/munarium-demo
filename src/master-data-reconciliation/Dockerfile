# SPDX-License-Identifier: Apache-2.0
FROM eclipse-temurin:21-jdk@sha256:1f79c73404fb0cccf9a3459eda22892f368d994b1028d6fb1ae871c1f49749a6
RUN apt-get update && apt-get install -y --no-install-recommends git fonts-dejavu-core && rm -rf /var/lib/apt/lists/*
RUN git init /opt/munarium && cd /opt/munarium && git remote add origin https://github.com/iokaio/munarium.git && git fetch --depth 1 origin bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3 && git checkout --detach FETCH_HEAD
ENV GRADLE_USER_HOME=/opt/gradle-cache JAVA_TOOL_OPTIONS=-Djava.awt.headless=true
WORKDIR /app
COPY settings.gradle.kts build.gradle.kts gradle.lockfile ./
RUN /opt/munarium/clients/java/gradlew --no-daemon --max-workers=2 dependencies cacheRuntime
COPY . .
RUN /opt/munarium/clients/java/gradlew --no-daemon --max-workers=2 installDist testClasses && /opt/munarium/clients/java/gradlew -p /opt/munarium/clients/java --no-daemon --max-workers=2 testClasses conformanceTestClasses
ENTRYPOINT ["/app/build/install/master-data-reconciliation/bin/master-data-reconciliation"]
