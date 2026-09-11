# Demo qualification profiles and resource checks

The thirteen applications have isolated Docker workflows. Their recorded results establish the tested fixture, application, client, service and host combination. A successful default workload does not establish stress capacity or native desktop behavior.

## Capacity preflight

Every demo's `local.ps1` and `local.sh` calls the shared [PowerShell](../tools/demo_preflight.ps1) or [POSIX](../tools/demo_preflight.sh) preflight before building. A pinned Python container reads available memory, CPU count, free Docker storage and free space on the output mount. It has no network, Docker socket, private oracle or provider credentials. Reports are retained under `artifacts/<demo>/preflight/`.

| Guardrail | Default and held-out | Stress |
|---|---:|---:|
| CPUs visible to the container | 2 | 2 |
| Available memory | 4 GiB; 8 GiB for inventory | 8 GiB |
| Free Docker storage | 15 GiB | 25 GiB |
| Free output storage | 1 GiB | 2 GiB |

These are conservative operational guardrails, not measured minimum requirements. The preflight also checks a local Linux Docker engine and Compose. Automated stacks publish no host service ports, so their normal test runs cannot collide with an occupied host port. A manually published desktop endpoint must be checked separately. Stopping a project does not require capacity checks.

## Measure a complete workload

The [PowerShell recorder](../tools/measure_demo.ps1) and [POSIX recorder](../tools/measure_demo.sh) execute the existing complete keyless test command in a fresh project. Use the demo's required project-name prefix:

```powershell
./tools/measure_demo.ps1 -Demo invoice-exception -Project invoice-measured -Profile default
```

```sh
sh tools/measure_demo.sh invoice-exception invoice-measured default
```

The recorder saves the workflow log, elapsed time, approximately three-second Docker CPU/memory/I/O samples, unpacked image sizes and project-volume logical sizes under `artifacts/<demo>/measurements/<run>/`. Samples can miss short-lived peaks; neither sampled maxima nor elapsed times are capacity guarantees. Failed workflows retain their reports and return failure. The tools preserve project containers and volumes for investigation; use that demo's documented stop/cleanup commands afterward.

The first recorded default invoice measurements passed the complete 29 application, 175 SDK and 20 business-case checks. PowerShell run `5baa4197fddb44638665ee1055a33cc7` took 43.70 seconds including report collection; POSIX run `20260911T072009Z-d9a827dabb134629` took 48 seconds through the workload. Sampled project memory peaked at 208,572,249 and 211,665,548 bytes respectively, and sampled project CPU at 99.76% and 100.81% (100% is one core). These exclude Docker/VM/build-daemon overhead and unobserved peaks. The PowerShell project's named volumes occupied 57,368,948 logical bytes. Dependencies were cached; these are reproducible workload observations, not cold-start or minimum-hardware benchmarks.

`DEMO_PROFILE` selects a fixture profile where the demo has a committed `fixture-profiles.json`. Unsupported profiles fail preflight. Each profile's fixture specification and measured outcome belongs in that demo's README. Held-out inputs must be generated from the separately declared regression seed; do not tune the answer prompt on those expected answers. Stress profiles remain keyless and use the controlled protocol fixture where completion is needed.

## Pinned images and download sizes

[demo_image_inventory.ps1](../tools/demo_image_inventory.ps1) reads the pinned public registry manifests without pulling image layers. The 2026-09-11 inventory recorded these compressed layer bytes for Linux/AMD64. Each listed digest also has a Linux/ARM64 manifest:

| Image | Compressed layer bytes, AMD64 |
|---|---:|
| Server 1.1.1 | 16,852,979 |
| pgvector PostgreSQL 16 | 156,176,731 |
| Python 3.12 slim | 46,175,163 |
| .NET SDK 10 | 350,887,004 |
| Temurin JDK 21 | 230,080,573 |
| Rust 1.98 | 592,953,245 |

These are per-image cold layer-transfer sizes before shared-layer caching, not total initial downloads. Application builds additionally fetch their pinned source checkout, packages and compiler dependencies. The measured workflow uses cached dependencies; its local unpacked image sizes and disk measurements are separate quantities. The inventory briefing builds Matrix from its unchanged pinned upstream Dockerfile, which currently selects Linux/AMD64 explicitly; its image is measured in that demo's report.

## Host and architecture qualification

Docker Desktop on Windows with Linux/AMD64 containers is the recorded baseline. Git Bash exercises the POSIX wrapper on that host. Ubuntu under WSL can additionally exercise Linux userspace and path handling, but does not establish a native Linux Engine deployment or macOS behavior. Registry ARM64 availability is not runtime qualification.

To explicitly select the AMD64 compatibility path on a host with Docker emulation enabled, set `DOCKER_DEFAULT_PLATFORM=linux/amd64` before running the same wrapper. In PowerShell use `$env:DOCKER_DEFAULT_PLATFORM='linux/amd64'`; in POSIX use `export DOCKER_DEFAULT_PLATFORM=linux/amd64`. This is especially relevant to Matrix's pinned AMD64 build. An AMD64 run on this Windows/x86_64 host executes natively and cannot qualify Apple Silicon emulation. Native Linux/macOS, native ARM64 and Apple Silicon emulation require results from those environments.

For the employee-policy desktop, record this supplementary checklist separately on Windows, Linux and macOS:

1. Launch the published native application and verify that text, controls and window scaling render correctly.
2. Select a provisioned identity, submit a question and observe actual progress.
3. Navigate to a returned source and inspect its excerpt and identity.
4. Switch identity and verify that the previous session and restricted sources are unavailable.
5. Export through the OS file picker to a chosen writable folder; open the saved answer and evidence files.
6. Record OS/architecture, application revision, result and any defect, then close the application.

Container headless tests remain required and cannot substitute for these OS-integration checks.

The [employee policy assistant walkthrough](demos/employee-policy-assistant/README.md) records the completed native Windows x64 check on 2026-09-11: launch, question entry, grounded answer/source inspection, Windows file-picker export and identity-state clearing. It used the held-out canned backend. Native Linux/macOS and ARM64 checks remain pending; the Windows result does not qualify those environments.
