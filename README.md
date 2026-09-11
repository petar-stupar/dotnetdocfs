# dotnetdocfs

.NET API documentation as a filesystem, served over 9P, so an agent reads it with `cat`, `ls` and
`grep` instead of a search box.

```text
~/mnt/dotnetdocfs/docs/framework/System.Text.Json/JsonSerializer/Serialize.md
```

That file does not exist on disk. It is rendered when you read it, from the reference assemblies
and XML documentation already installed on your machine. **No documentation is shipped, vendored
or downloaded** — none is bundled with this repository and none is fetched at runtime. Restore a
NuGet package and `ctl refresh` puts it in the tree; uninstall an SDK and it is gone. (Mounting on
macOS or Windows does fetch something: the bridge container is built from `alpine:3` and a Samba
package. The documentation never is.)

Built on [`ninep`](https://github.com/petar-stupar/9p-csharp), the 9P2000 / 9P2000.u / 9P2000.L
implementation for .NET.

## Why a filesystem

An agent with filesystem tools already knows how to read a directory, follow a link and grep a
tree. Handing it documentation as files means no new tool, no API to learn, and no tokens spent
on a search result page. 9P is the simplest way to present something as a filesystem without
writing a kernel module: the server is a tree of small handlers, and the kernel mounts it.

## What it serves

```text
/index.md                                       how the tree is laid out
/ctl                                            write a command, read the command list
/skills/dotnet-api-docs/SKILL.md                an agent skill for navigating this
/docs/framework/<namespace>/<Type>/index.md     the .NET base class library
/docs/framework/<namespace>/<Type>/<Member>.md  one page per member name
/docs/packages/<package>/<version>/...          your NuGet cache, same shape
/docs/ingested/<assembly>/...                   whatever /ctl was told about
```

Pages are [Open Knowledge Format](https://github.com/GoogleCloudPlatform/knowledge-catalog/tree/main/okf):
markdown with YAML frontmatter, an `index.md` at every level for progressive disclosure, and
cross-links as ordinary relative paths.

Two spellings differ from C#, because a filesystem cannot carry the originals. Generic arity is a
dash — `List<T>` is `List-1` — and constructors are `constructors.md`, since `.ctor` would be a
hidden file. A namespace is one directory with its full dotted name, and all overloads of a name
share one page.

### Links resolve

A cross-reference into the API becomes a relative link that works as a file read:

```markdown
Converts the provided value into a [JsonDocument](../JsonDocument/index.md).
```

A cross-reference to something this machine does not have is rendered as code, never as a link, so
following a link here always lands somewhere — 204,636 internal links across the whole framework
area, none of them broken. Links out to the web are the documentation author's own and are left
exactly as written; a link that is neither, which in the reference packs is always a `cref`
written as an `href` by mistake, is named rather than linked.

## Install

```text
curl -fsSL https://raw.githubusercontent.com/petar-stupar/dotnetdocfs/main/scripts/install.sh | sh
```

```powershell
irm https://raw.githubusercontent.com/petar-stupar/dotnetdocfs/main/scripts/install.ps1 | iex
```

Both check the archive against the release's published `SHA256SUMS` before installing anything,
and need no root. That catches a truncated or corrupted download. It is not a signature: the sums
come from the same release, over the same connection, so it says the bytes arrived intact rather
than that they are the bytes I built.

A piped script takes no arguments of its own, so pinning a version or choosing a directory means
passing them through:

```text
curl -fsSL .../install.sh | sh -s -- --version v0.1.0 --bin-dir ~/bin
```

```powershell
& ([scriptblock]::Create((irm .../install.ps1))) -Version v0.1.0 -BinDir C:\tools
```

### By hand

Take the archive for your platform from the
[releases page](https://github.com/petar-stupar/dotnetdocfs/releases) — `osx-arm64`, `osx-x64`,
`linux-x64`, `linux-arm64`, `win-x64` or `win-arm64` — and put the binary somewhere on your `PATH`:

| | Where | Already on `PATH`? |
| --- | --- | --- |
| macOS, Linux | `~/.local/bin` | usually; `/usr/local/bin` always, and needs `sudo` |
| Windows | `%LOCALAPPDATA%\Programs\dotnetdoc` | no, add it |

```text
tar -xzf dotnetdoc-0.1.0-osx-arm64.tar.gz
install -m 755 dotnetdoc ~/.local/bin/dotnetdoc
```

If `~/.local/bin` is not on your `PATH`, add it:

```text
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zshrc    # or ~/.bashrc
```

The builds are **self-contained**: they carry their own .NET and run on a machine that has none.
What they *serve* still comes from the .NET installed on that machine, so a machine without an SDK
has an empty `docs/framework`.

A binary downloaded through a **browser** on macOS is quarantined by Gatekeeper and refuses to run,
because these are not code-signed. `curl` and the install script do not set that attribute; if you
did download it through a browser, clear it with
`xattr -d com.apple.quarantine ./dotnetdoc`.

### From source

```text
dotnet publish src/DotnetDocFs -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

## Run it

```text
dotnetdoc                      serve over 9P and print the address
dotnetdoc --mount              serve, then mount it at ~/mnt/dotnetdocfs
dotnetdoc --mount --path DIR   mount somewhere else
dotnetdoc --unmount            unmount and remove the bridge
dotnetdoc --help               every flag
```

`--dotnet-root` and `--package-root` choose what is served; with neither, `DOTNET_ROOT` and
`NUGET_PACKAGES` are consulted before the usual install locations.

`--mount` does the best thing your platform can do:

| Platform | How |
| --- | --- |
| Linux | the kernel mounts 9P directly |
| macOS | a container mounts 9P and re-exports it over SMB, which macOS mounts |
| Windows | not automated; run `dotnetdoc` inside WSL and mount there, then read it at `\\wsl$\<distro>\...` |

**macOS does not have a usable 9P client.** `/sbin/mount_9p` only mounts a VM share by tag, and
`9pfuse` is not in Homebrew — it lives inside plan9port, which is not either. macFUSE would mean a
Reduced Security reboot on Apple Silicon plus building plan9port from source. The container route
needs neither: Docker Desktop's kernel has v9fs built in, and macOS mounts SMB with no kernel
extension and no `sudo`.

`--mount-docker` forces that route everywhere. `--restart-docker-container` recreates the bridge.

Stopping the server unmounts and removes the bridge, whether you press Ctrl-C or `kill` it.
`SIGKILL` cannot be caught by anything, so a mount can still be orphaned; `dotnetdoc --unmount`
clears one up and is safe to run when nothing is mounted.

### Refusals in the log are normal

`--log-requests` reports every kind of request refused, and a healthy SMB mount refuses three of
them. All three are a client asking for something optional and being told, correctly, that this
tree does not do it:

| Refusal | Who asks, and why |
| --- | --- |
| `Txattrwalk` -> `EOPNOTSUPP` | Samba asks for extended attributes whenever it opens a file. This tree has none. |
| `Tlock` -> `EOPNOTSUPP` | The macOS SMB client takes byte-range locks; Samba maps them onto POSIX locks. A generated read-only tree has nothing to lock. |
| `Twalk <ip>` -> `ENOENT` | Samba answers a disk-quota request with `quotactl` on the filesystem's device, which for a 9P mount is the server's address as a string. The kernel looks that name up as a path, and no such file exists. |

Granting the first two would mean inventing attributes and promising locks that cannot be enforced,
so they stay refused. The third is a lookup of a name that genuinely is not there. Nothing is wrong
when these appear; the line that matters is `verified: ... reads back as the catalog index`.

### macOS will refuse to read the mount until you allow it

An SMB mount is a network volume, and macOS gates those per application:

> System Settings → Privacy & Security → Files and Folders → *your terminal* → **Network Volumes**

A terminal launched from the Finder is asked once with a dialog. One started by another program is
refused with no prompt and no log entry, which reads as a broken mount rather than a missing
permission. `--mount` reads a page back after mounting and prints this if it happens; the mount is
left up, so granting the permission is enough.

## Adding a library

Anything restored into your NuGet cache is already under `/docs/packages`. For an assembly that is
not, point `/ctl` at it:

```text
echo 'ingest /path/to/Your.Library.dll' > ~/mnt/dotnetdocfs/ctl
```

It appears under `docs/ingested/Your.Library/`, with prose from the `.xml` beside it if the build
produced one.

The path is resolved by the **server**, on the machine running `dotnetdoc` — not relative to the
mount, and not inside the bridge container. Give it the path you would give `ls`.

The assembly is **inspected, never loaded**: everything goes through `MetadataLoadContext` and the
metadata tables, so no static constructor, module initializer or any other code from an inspected
assembly runs. That is what makes accepting a path from a caller reasonable at all.

`/ctl` takes one command per line — `ingest <path>`, `forget <name>`, `refresh` — and a command
that fails fails the write. Reading it returns that list, so `cat ctl` tells you what it accepts.
It is open by default; `--no-ctl` serves a wholly read-only tree.

`refresh` forgets everything ingested and everything already listed, so the next walk reads the
machine again. That is what to run after restoring a package: a directory keeps its entries once
something has listed it, which is what makes a path stay meaningful under a mounted client.

A failed command carries its reason, but a mount will not show it to you: 9P2000.L, which is what
the kernel speaks, carries an error number and no text, so a shell reports `Invalid argument` and
nothing more. The server's own output has the sentence.

The server listens on loopback by default, including when the bridge container is the one reading
it — Docker Desktop forwards `host.docker.internal` to the host's loopback — so nothing off this
machine reaches the tree or the control file. There is no authentication behind that: whoever can
open the socket gets the root, which on loopback means any other user of this machine. `--listen`
can bind wider, and is refused unless `--no-ctl` is given too, or the mount flags are absent.

## Speed

Nothing is cached on disk, so there is no file to go stale and nothing to invalidate. Indexing the
whole `net10.0` reference set — 307 assemblies, 335 namespaces — takes about **35 ms**, and it
reads names out of the metadata tables without resolving or loading anything. Opening the first
page under `System.Text.Json`, which parses the 7 MB XML file behind it, is about **12 ms**; the
next page out of that same file is about **1 ms**.

That is the whole reason the tree can be generated per read: listing a namespace never opens a 7 MB
XML file, and nothing is parsed until a page below it is actually opened.

In memory there *is* a cache, and it is bounded: the eight most recently used XML documentation
files kept parsed, the members of the five hundred most recently described types, and the metadata
contexts of the twenty-four most recently read assemblies, each holding one file handle. A rendered
page's bytes are held while something has the file open and weakly after that, and the render is
deterministic, so a re-render is byte-for-byte the first one. What grows with use is the tree
itself — a directory that has been listed keeps its entries, which is what makes a walked path
stable — and that has a ceiling rather than a slope. Reading every one of the 55,771 pages in the
framework area, which is what `grep -r` over the whole tree does, takes about 3 s and settles at
about **260 MB** of managed heap, and does not climb from there; `echo refresh > ctl` drops all of
it. Resident memory is roughly **480 MB** at that point, most of the difference being heap the
collector has not handed back to the operating system.

## Build

```text
dotnet build && dotnet test
```

Needs the .NET 10 SDK. `src/DotnetDocFs.Catalog` knows nothing about 9P and `src/DotnetDocFs` knows
nothing about .NET metadata; the 9P layer is a few hundred lines between them.

## Licence

MIT — see [LICENSE](LICENSE). Contributions are welcome; [CONTRIBUTING.md](CONTRIBUTING.md) says
how they land, and [docs/releasing.md](docs/releasing.md) how a version gets cut.
