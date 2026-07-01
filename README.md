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

| Revit Version | Target Framework | API Package |
| --- | --- | --- |
| 2025 | net8.0-windows | Nice3point Revit API 2025.* |
| 2026 | net8.0-windows | Nice3point Revit API 2026.* |
| 2027 | net10.0-windows | Nice3point Revit API 2027.* |

## Building
Open `src/Fetch.sln` in Visual Studio and select the matching configuration for the Revit version you want to build: `Release R25`, `Release R26`, or `Release R27`. Debug builds use the matching `Debug R25`, `Debug R26`, or `Debug R27` configuration. All supported Revit versions build from the single `src/Fetch/Fetch.csproj` project file.

## Known Issues
- N/A

## Package Sources
FETCH can sync packages from a local folder, a direct HTTP/HTTPS zip URL, a public Google Drive file link, a public OneDrive/SharePoint file link, or a public GitHub release. Private cloud links that require sign-in are not supported.

For GitHub releases, use either a release page such as `https://github.com/org/repo/releases/latest` or `https://github.com/org/repo/releases/tag/v1.0.0`, or a direct release asset URL such as `https://github.com/org/repo/releases/download/v1.0.0/packages.zip`. Release pages must include an uploaded `.zip` release asset; FETCH selects the first `.zip` asset.

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
If you're interested in contributing to FETCH, just submit a [pull request](https://github.com/johnpierson/FETCH/pulls) or a [feature request](https://github.com/johnpierson/FETCH/issues) .

