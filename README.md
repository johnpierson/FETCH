<h1 align="center">
  <br>
  <img src="/_documentation/FetchLogo.png" alt="FETCH" width="400">
  <br>
</h1>

<h3 align="center">A plugin for Revit to help manage Dynamo packages.</h3>

![GitHub release (latest by date)](https://img.shields.io/github/v/release/johnpierson/FETCH?include_prereleases)
[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg)](https://github.com/johnpierson/FETCH/graphs/commit-activity)
[![GitHub license](https://img.shields.io/github/license/johnpierson/FETCH)](https://github.com/johnpierson/FETCH/blob/main/LICENSE)

## LICENSE
This code is licensed primarily under [BSD 3-Clause](https://github.com/johnpierson/FETCH/blob/main/LICENSE) with a [Commons Clause License](https://commonsclause.com/) attached to that.

## Supported Versions
FETCH supports Revit 2025, Revit 2026, and Revit 2027 only.

The installer deploys FETCH per user without requiring administrator rights. Add-in manifests, binaries, and settings are installed under:

```text
%APPDATA%\Autodesk\Revit\Addins\<Revit Version>\
  Fetch.addin
  FETCH\
    Fetch.dll
    FetchSettings.ini
```

| Revit Version | Target Framework | API Package |
| --- | --- | --- |
| 2025 | net8.0-windows | Nice3point Revit API 2025.4.50 |
| 2026 | net8.0-windows | Nice3point Revit API 2026.4.10 |
| 2027 | net10.0-windows | Nice3point Revit API 2027.2.0 |

## Building
Open `src/Fetch.sln` in Visual Studio and select the matching configuration for the Revit version you want to build: `Release R25`, `Release R26`, or `Release R27`. Debug builds use the matching `Debug R25`, `Debug R26`, or `Debug R27` configuration. All supported Revit versions build from the single `src/Fetch/Fetch.csproj` project file. Revit itself is not needed to build or test.

Building and testing is covered in [CONTRIBUTING.md](CONTRIBUTING.md), including the fact that a **Debug** build installs the add-in into your live Revit add-ins folder so F5 debugging works.

```bash
dotnet test src/Fetch.sln --configuration "Release R25"
```

## Logging
Each run appends to `FetchLog.txt` next to the installed `Fetch.dll` (the same folder as `FetchSettings.ini`), falling back to `%LOCALAPPDATA%\FETCH\FetchLog.txt` when that folder is not writable. The log records the resolved Dynamo version and package folder, the configured source, what was replaced, and any failure — including cases FETCH handles quietly, such as skipping the sync when the source cannot be reached. The file rolls to `FetchLog.previous.txt` once it passes 1 MB.

It is the first thing to look at when someone reports that their packages did not update.

## How syncing replaces content
On every Revit startup FETCH **replaces** the Dynamo package folder with the contents of the configured source. Packages you installed yourself through the Dynamo Package Manager are not merged and will be removed if they are not part of the deployment.

The replacement is staged: content is copied into a temporary sibling folder first and only swapped into place once that copy has fully succeeded, so a failed or interrupted sync leaves your existing packages as they were. FETCH also refuses to sync at all when the source does not look like a Dynamo package folder, rather than copying unexpected content over your packages.

Deployed graphs are replaced the same way, and because those files may contain your edits the previous folder is kept alongside it as `<graph folder>.fetch-backup` (one generation).

If two Revit sessions of the same version start at once, the second waits for the first to finish rather than both writing to the same folder.

## When the sync runs
FETCH starts the sync in the background as Revit launches; Revit does not wait for it. A successful sync is silent and recorded in the log. Only a problem you can act on raises a dialog, once, after Revit is idle.

Starting Revit with no network access is treated as normal: FETCH leaves the installed packages alone, notes it in the log, and does not interrupt you. A download that stops making progress for 100 seconds is abandoned rather than left hanging, while a slow but advancing download is allowed to finish however long it takes.

Because the sync no longer blocks startup, opening Dynamo within a few seconds of launching Revit may still show the previous packages. The log records when the sync finished.

If another Revit session of the same version already has Dynamo open, FETCH skips the sync rather than replacing package files that session is using, and tells you so. Close the other session and restart Revit to pick up the update.

## Known Issues
- Packages installed manually through the Dynamo Package Manager are removed by a sync unless the deployment includes them.
- Opening Dynamo immediately after Revit starts can show the previous packages, because the sync runs in the background.

## Package Sources
FETCH can sync packages from a local folder, a direct **HTTPS** zip URL, a public Google Drive file link, a public OneDrive/SharePoint file link, or a public GitHub release. Private cloud links that require sign-in are not supported.

Downloaded packages are loaded into Revit as code, so plain `http://` sources are rejected: anyone on the network could replace them in transit. Use `https://`. If you genuinely need plain HTTP, set `AllowInsecureHttp=true` (see below); `http://localhost` is always allowed for testing.

Before anything is installed, FETCH checks that the download really is a zip archive, and reports a specific error if the link returned a web page instead — which is what a sharing page or a sign-in prompt looks like to a downloader.

### Settings
`FetchSettings.ini` is written by the installer and lives beside `Fetch.dll`. All keys go in the `[Settings]` section.

| Key | Required | Meaning |
| --- | --- | --- |
| `Path` | yes | Local/UNC folder, or an `https://` link to a package zip |
| `GraphPath` | no | Root for deployed Dynamo graphs; defaults to `Documents\FETCH Dynamo Graphs` |
| `Sha256` | no | Expected SHA-256 of the downloaded zip; the sync is refused if it does not match |
| `AllowInsecureHttp` | no | Set to `true` to permit plain-HTTP sources |

```ini
[Settings]
Path=https://github.com/org/repo/releases/latest
GraphPath=
Sha256=
AllowInsecureHttp=
```

Pinning `Sha256` is worthwhile for a fixed release: it guarantees every machine installs exactly the archive you published. Leave it empty when the source is a moving target such as `releases/latest`.

For GitHub deployments, use a release page such as `https://github.com/johnpierson/FETCH-Sample-Dynamo-Deployment/releases/latest` or `https://github.com/org/repo/releases/tag/v1.0.0`, or a direct release asset URL such as `https://github.com/org/repo/releases/download/v1.0.0/packages.zip`. Release pages must include an uploaded `.zip` asset; FETCH selects the first `.zip` asset.

FETCH also supports a firm-managed source folder or zip with this structure:

```text
Overall Folder
  2025
    Packages
    Dynamo Graphs
  2026
    Packages
    Dynamo Graphs
  2027
    Packages
    Dynamo Graphs
```

Version folders can be named `2025`, `Revit 2025`, or `R25` style for the supported 2025-2027 range. `Packages` sync to the matching Dynamo package location for the running Revit/Dynamo version. `Dynamo Graphs` sync to `Documents\FETCH Dynamo Graphs\<Revit Version>` unless the installer provides a custom Dynamo graph root folder.

## Contributors
This package is primarily managed by the author of http://designtechunraveled.com and by [People Like You™](https://github.com/johnpierson/FETCH/graphs/contributors).

## Help improve FETCH
If you're interested in contributing to FETCH, read [CONTRIBUTING.md](CONTRIBUTING.md), then submit a [pull request](https://github.com/johnpierson/FETCH/pulls) or a [feature request](https://github.com/johnpierson/FETCH/issues). Notable changes are recorded in [CHANGELOG.md](CHANGELOG.md).

