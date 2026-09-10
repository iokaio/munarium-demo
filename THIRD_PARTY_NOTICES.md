# Third-party notices

Preserve this file and the bundled license texts when distributing the application.
The application license does not relicense third-party components or source data.

## Browser assets

| Component | Version | License and retained notice |
|---|---|---|
| Bootstrap CSS and bundled JavaScript | 5.3.0 | MIT; [license](licenses/bootstrap-MIT.txt) |
| Popper bundled in Bootstrap | 2.11.7 | MIT; [license](licenses/popper-MIT.txt) |
| Material Design Icons code and webfonts | 5.9.55 | MIT code, Apache-2.0 fonts/icons; [upstream notice](licenses/material-design-icons.txt) and [Apache text](LICENSE) |

Bootstrap and icon files retain their upstream headers. The demo's replacement base
stylesheet and SVG mark are original application assets. Third-party template assets
are not distributed.

## .NET dependencies

The resolved NuGet graph is recorded in `src/Demo.Web/packages.lock.json`.

| Package | License |
|---|---|
| `DnsClient/1.8.0` | Apache-2.0 |
| `Microsoft.Data.Sqlite/9.0.8` | MIT |
| `Microsoft.Data.Sqlite.Core/9.0.8` | MIT |
| `SQLite/3.53.4` | Public domain |
| `SQLitePCLRaw.bundle_e_sqlite3/3.0.5` | Apache-2.0 |
| `SQLitePCLRaw.config.e_sqlite3/3.0.5` | Apache-2.0 |
| `SQLitePCLRaw.core/3.0.5` | Apache-2.0 |
| `SQLitePCLRaw.provider.e_sqlite3/3.0.5` | Apache-2.0 |

Microsoft packages are copyright Microsoft and contributors; see [MIT text](licenses/dotnet-MIT.txt).
DnsClient is copyright MichaCo and contributors. SQLitePCLRaw is copyright Eric Sink
and contributors; both use [Apache 2.0](LICENSE). SQLite is public domain; see its
[retained notice](licenses/sqlite.txt). The .NET runtime in Microsoft's base images
carries its own third-party notices and licenses, which must be retained with those images.

## Build and operation tools

Playwright 1.55.1 is Apache-2.0 (Microsoft and contributors); its downloaded browser
has separate Chromium third-party notices. PyYAML 6.0.3 is MIT; see the
[retained license](licenses/pyyaml-MIT.txt). Tool versions and integrity hashes are
recorded in the package lockfiles. Container images retain their own licenses:
Munarium Server Apache-2.0, PostgreSQL PostgreSQL License, pgvector PostgreSQL License,
and Ollama MIT; Ollama model weights have their own model licenses.

## Data

See [data/RIGHTS.md](data/RIGHTS.md) for synthetic corpus licensing and original-source
rights in Library of Congress and USPTO material. Preserve source URLs and record IDs.
