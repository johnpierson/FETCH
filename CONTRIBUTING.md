# Contributing to FETCH

Thanks for helping out. FETCH replaces content in people's Dynamo folders on every Revit startup, so the bar for changes in the sync path is deliberately high.

## Getting set up

You need the .NET SDK (8.0 and 10.0 are both used) and, for installer work, [Inno Setup 6](https://jrsoftware.org/isdl.php). Revit itself is **not** required to build or run the tests.

```bash
dotnet test src/Fetch.sln --configuration "Release R25"
```

Pick the configuration that matches the Revit version you care about: `R25`, `R26`, or `R27`, in `Debug` or `Release`.

## What a build touches

A **Debug** build installs the add-in into your live Revit add-ins folder (`%APPDATA%\Autodesk\Revit\Addins\<version>`) so you can press F5 and debug against real Revit. A **Release** build does not.

Override it either way with `-p:FetchInstallLocal=true|false`. CI and the test project always pass `false`.

## Ground rules for the sync path

The code under `src/Fetch/Sync` deletes and replaces user content. Two rules apply there:

1. **Never delete before the replacement is ready.** Content is staged into a sibling folder and swapped in only after the copy fully succeeds. If you add a failure path, make sure the user's existing packages survive it.
2. **Never trust the source.** A downloaded archive is checked for a zip signature (and an optional SHA-256) before extraction, and a package folder that was *guessed* rather than positively identified must contain a real Dynamo package before anything is replaced.

If you change either behaviour, the tests in `PackageSyncTests` that inject failures should fail. If they still pass, the test is wrong or the change did not do what you think.

## Tests

Tests live in `tests/Fetch.Tests` and need neither Revit nor network access — the downloader tests run against a loopback socket server. Pure logic (layout resolution, URL rewriting, version selection, integrity checks) is `internal` and reachable via `InternalsVisibleTo`.

New behaviour needs a test. Bug fixes need a test that fails without the fix.

## Releasing

The version lives in exactly one place: the `<Version>` property in `src/Fetch/Fetch.csproj`. The assembly attributes, the installer, and the CI tag check all read from it.

To release: bump `<Version>`, commit, then tag `vX.Y.Z`. CI verifies the tag matches the project version, builds all three Revit configurations, compiles the installer, and publishes the release. Build outputs (`_Release/`, `installers/Output/`) are not committed — CI produces them.
