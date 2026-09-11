# Contributing

Thank you for looking at this. One person reviews everything here, so the workflow is deliberately
short.

## How changes land

`main` is protected: nothing is pushed to it directly and only the owner merges.

1. Fork the repository on GitHub and clone your fork.
2. Branch from `main` (`git switch -c fix/what-it-fixes`).
3. Make the change, with a test that fails without it.
4. Run the gate until it is green.
5. Push to your fork and open a pull request against `petar-stupar/dotnetdocfs:main`. CI runs the
   same gate on Linux, macOS and Windows, and publishes the self-contained shape a release uses;
   a red run is not reviewed.

Small, single-purpose pull requests are merged fastest. If a change is large, or changes what the
tree looks like to something already reading it, open an issue first.

## The gate

```text
dotnet build -warnaserror && dotnet test && dotnet format --verify-no-changes
```

Needs the .NET 10 SDK. Warnings are failures, and a suppression carries its reason where it is
suppressed.

## Tests

There are not many, and that is the intent: the tree is generated from whatever .NET the machine
has, so the useful tests are the ones that do not depend on which. Test what a reader would
notice — a page that renders, a link that resolves, a directory that answers after a `refresh` —
and the boundaries around it. `--unmount` is the exception and is tested hard: it is the only
destructive thing this program does, so `MountGuardTests` pins its predicate against real
`mount(8)` output from Linux, macOS and WSL.

A test that needs a .NET installation to be interesting calls `Assert.SkipWhen` rather than
failing, so the suite stays honest on a machine that has none.

## What belongs where

`src/DotnetDocFs.Catalog` knows nothing about 9P: it discovers assemblies, reads metadata and
renders markdown. `src/DotnetDocFs` knows nothing about .NET metadata: it serves a tree and mounts
it. The 9P layer between them is a few hundred lines and should stay that way. An `internal` type
lives under an `Internal/` directory.

## Documentation

The README and the CHANGELOG are part of the change, not a follow-up. A measured claim carries its
measurement — the numbers in the README are real ones from a real run, and a change that moves one
re-measures it rather than rounding the old number.

## Releasing

A `v*` tag on `main` builds every platform and publishes a GitHub release.
[docs/releasing.md](docs/releasing.md) has the procedure. Nothing is released per commit.
