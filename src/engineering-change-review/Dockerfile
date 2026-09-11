# SPDX-License-Identifier: Apache-2.0
FROM rust:1.98@sha256:620dbcd124499c59e2406d3741574b5c5838cf9eb9656f0c3a03948f79b02959
ENV RUSTUP_TOOLCHAIN=1.98.0 CARGO_TARGET_DIR=/opt/target CARGO_BUILD_JOBS=2
RUN apt-get update && apt-get install -y --no-install-recommends git protobuf-compiler python3-pil fonts-dejavu-core && rm -rf /var/lib/apt/lists/* && rustup component add rustfmt clippy
RUN git init /opt/munarium && cd /opt/munarium && git remote add origin https://github.com/iokaio/munarium.git && git fetch --depth 1 origin bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3 && git checkout --detach FETCH_HEAD
RUN cd /opt/munarium/clients/rust && cargo test --workspace --locked --no-run
WORKDIR /app
COPY Cargo.toml Cargo.lock ./
RUN mkdir src && echo 'fn main() {}' > src/main.rs && cargo build --locked
COPY . .
RUN touch src/main.rs && cargo build --locked --offline && cargo test --locked --offline --no-run
ENTRYPOINT ["/opt/target/debug/engineering-change-review"]
