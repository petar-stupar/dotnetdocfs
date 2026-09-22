# Changelog

Notable changes, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the versions follow [semantic versioning](https://semver.org/spec/v2.0.0.html).

The `## [<version>]` section of a release is its release notes, verbatim; the release workflow
refuses a tag whose version has no section here.

## [Unreleased]

## [0.1.3] — 2026-09-22

### Changed

- **The served `SKILL.md` names the mountpoint** when this server was told one — because it did
  the mounting, or because `--path` said where you would mount it yourself. Otherwise it keeps
  writing `<mount>` for you to replace: a path nobody stated would be a guess, and a skill naming
  a directory that is not there is worse than one that asks to be filled in. `--path` now means
  something without `--mount` for exactly this. If the agent reading the skill is in a different
  filesystem namespace from the server — a container that bind-mounts the host's mountpoint
  elsewhere — the path is still the server's, and there is no flag for that yet.
- Only `<mount>` is substituted. `<namespace>`, `<Type>`, `<version>` and the rest of the skill's
  placeholders are ones a reader fills in, and they are left alone.

## [0.1.2] — 2026-09-16

### Added

- **`--version`** reports this program's version and the version of the 9P library under it:

  ```text
  dotnetdoc 0.1.2
  NineP.Server 0.4.1
  NineP.Protocol 0.4.1
  ```

  Read from the loaded assemblies rather than from a constant. A published build carries the
  library inside it, so which `NineP` a binary uses cannot be answered from the repository once
  the binary has left it — which is when the question gets asked. Printed before the flag
  combinations are checked, as `--help` is: asking what a binary is should not be refused over
  something unrelated to the answer.

## [0.1.1] — 2026-09-16

### Fixed

- **A recursive walk no longer wedges the mount.** The bridge's 9P mount and the direct Linux one
  both use `cache=none` now: a caching mode pins one fid per cached dentry for as long as the
  dentry cache holds it, the whole mount shares one connection and one fid table, and the server
  caps fids per connection at 65536 — so a `find` or `grep -r` over the 72,282 directories at type
  depth ran past the cap and every open after it was refused `Too many open files` until the mount
  was remade. Uncached dentries are released on the spot and the fid with them; `find -maxdepth 4`
  over the SMB mount now answers all 72,282 directories in 168 s without an error.

### Changed

- `NineP.Server` 0.4.0 → 0.4.1, which carries the mount guidance this fix came from, and the
  test-and-tooling group moves forward. Nothing in the library behaves differently.

## [0.1.0] — 2026-09-11

### Added

- **.NET API documentation served over 9P**, rendered when a page is read from the reference
  assemblies and XML documentation already on the machine. Nothing is vendored, shipped or
  downloaded: restore a package and it is in the tree.
- **The Open Knowledge Format layout** — an `index.md` at every level, YAML frontmatter whose
  `type` names what each page describes, and cross-references written as relative links that
  resolve as ordinary file reads. A reference to something the machine does not have is rendered
  as code rather than as a link, so a link here always leads somewhere, and links out to the web
  are left as their author wrote them.
- **`/docs/framework`** from the newest installed reference pack, **`/docs/packages`** from the
  NuGet cache, and **`/docs/ingested`** for assemblies named through `/ctl`.
- **`/ctl`**, which takes `ingest <path>`, `forget <name>` and `refresh`, one per line, and returns
  that list when read. An ingested assembly is inspected through `MetadataLoadContext` and the
  metadata tables, never loaded: no code from it runs.
- **`/skills/dotnet-api-docs/SKILL.md`**, an agent skill for navigating the tree.
- **Mounting.** `--mount` mounts 9P directly on Linux and goes through a container that re-exports
  SMB on macOS, which needs no kernel extension and no `sudo`. `--unmount` detaches and removes the
  bridge, and refuses a path that is not one of ours. `--restart-docker-container` recreates it.
  Stopping the server unmounts, on `SIGINT`, `SIGTERM` or `SIGHUP`.
- **A mount is verified, not assumed**: a page is read back through it, and the macOS
  network-volume permission — which is refused silently, with no prompt and no log entry — is
  reported with the setting to change.
- **`--log-requests`**, which names each kind of 9P request the first time it arrives and each kind
  of refusal once.

### Notes

- **`refresh` reaches a client that is already holding a directory.** A 9P client keeps a fid per
  directory it has walked into and a `cache=loose` mount holds them for a long time, so
  invalidating from the root reached only what a fresh walk would have rebuilt anyway. Every
  directory now records the generation it was built under, and `refresh` moves the generation —
  and the version every qid carries, so a client caching on the qid is told that what it holds for
  a path is no longer what the path holds.
- **All three reference packs contribute to the framework area.** `Microsoft.NETCore.App.Ref`,
  `Microsoft.AspNetCore.App.Ref` and `Microsoft.WindowsDesktop.App.Ref` are separate products
  patched on their own schedules; the newest version of each is chosen and the three merged.
- **`--unmount` checks the device, not the filesystem type.** Inside WSL every Windows drive is a
  9p mount, and the README sends Windows users to WSL.
- Built on [`NineP.Server` 0.4.0](https://github.com/petar-stupar/9p-csharp/blob/main/CHANGELOG.md),
  which is where the `.L` owner-id and `Tsetattr` fixes this project needed landed. On 0.3.0 a
  mounted tree showed every file as `nobody` and answered `Value too large for data type` to
  anything needing an owner, and a `>` redirect to `/ctl` was refused `Operation not permitted`.
- **What is held in memory is bounded.** Nothing is cached on disk. In memory: the eight most
  recently used XML documentation files, the members of the five hundred most recently described
  types, and the metadata contexts of the twenty-four most recently read assemblies, each of which
  holds a file handle per assembly. A rendered page's bytes are held while something has the file
  open and weakly after that, and rendering is deterministic so a re-render is byte-for-byte the
  first. Reading every one of the 55,771 pages of the framework area — what `grep -r` over the
  tree does — settles around 260 MB of managed heap and does not climb from there.
- **`refresh` drops what has been listed**, not only what has been ingested, so restoring a NuGet
  package and running `echo refresh > ctl` puts it in the tree without a restart.
- **`--listen` off loopback is refused** unless `--no-ctl` is given, and cannot be combined with a
  mount. There is no authentication on the tree, so the address it binds is the whole of the
  access control.

[Unreleased]: https://github.com/petar-stupar/dotnetdocfs/compare/v0.1.3...HEAD
[0.1.3]: https://github.com/petar-stupar/dotnetdocfs/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/petar-stupar/dotnetdocfs/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/petar-stupar/dotnetdocfs/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/petar-stupar/dotnetdocfs/releases/tag/v0.1.0
