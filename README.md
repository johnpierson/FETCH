<h1 align="center">
  <br>
  <img src="/_documentation/FetchLogo.png" alt="FETCH" width="400">
  <br>
</h1>

<h3 align="center">A plugin for Revit to help manage Dynamo packages.</h3>

![GitHub release (latest by date)](https://img.shields.io/github/v/release/johnpierson/FETCH?include_prereleases)
[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg)](https://github.com/johnpierson/FETCH/graphs/commit-activity)
[![GitHub license](https://img.shields.io/github/license/johnpierson/FETCH)](https://github.com/johnpierson/FETCH/blob/main/LICENSE)


 _If you feel so inclined, here is a method to donate to this project_

 <a href="https://www.buymeacoffee.com/j0hnp" target="_blank"><img src="https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png" alt="Buy Me A Coffee" style="height: 41px !important;width: 174px !important;box-shadow: 0px 3px 2px 0px rgba(190, 190, 190, 0.5) !important;-webkit-box-shadow: 0px 3px 2px 0px rgba(190, 190, 190, 0.5) !important;" ></a>

## LICENSE
This code is licensed primarily under [BSD 3-Clause](https://github.com/johnpierson/FETCH/blob/master/LICENSE) with a [Commons Clause License](https://commonsclause.com/) attached to that.

## Current Version
FETCH is currently built against the following Revit versions:

| Revit Version | Target Framework | API Package |
| --- | --- | --- |
| 2021 | .NET Framework 4.8 | Revit_All_Main_Versions_API_x64 2021.0.0 |
| 2022 | .NET Framework 4.8 | Revit_All_Main_Versions_API_x64 2022.0.0 |
| 2023 | .NET Framework 4.8 | Revit_All_Main_Versions_API_x64 2023.0.0 |
| 2024 | .NET Framework 4.8 | Revit_All_Main_Versions_API_x64 2024.0.0 |
| 2025 | net8.0-windows | Nice3point Revit API 2025.* |
| 2026 | net8.0-windows | Nice3point Revit API 2026.* |
| 2027 | net10.0-windows | Nice3point Revit API 2027.* |

## Building
Open `src/Fetch.sln` in Visual Studio and select the matching configuration for the Revit version you want to build, such as `Release R25` or `Debug R27`. All supported Revit versions build from the single `src/Fetch/Fetch.csproj` project file.

## Known Issues
- N/A

## Package Sources
FETCH can sync packages from a local folder, a direct HTTP/HTTPS zip URL, a public Google Drive file link, or a public OneDrive/SharePoint file link. Private cloud links that require sign-in are not supported.

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

Version folders can be named `2025`, `Revit 2025`, or `R25` style. `Packages` sync to the matching Dynamo package location for the running Revit/Dynamo version. `Dynamo Graphs` sync to `Documents\FETCH Dynamo Graphs\<Revit Version>` unless the installer provides a custom Dynamo graph root folder.

## Contributors
This package is primarily managed by the author of http://designtechunraveled.com and by [People Like You™](https://github.com/johnpierson/FETCH/graphs/contributors).

## Help improve FETCH
If you're interested in contributing to FETCH, just submit a [pull request](https://github.com/johnpierson/FETCH/pulls) or a [feature request](https://github.com/johnpierson/FETCH/issues) .

