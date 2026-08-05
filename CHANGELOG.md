# Changelog

All notable changes to FETCH are recorded here. Versions follow the `<Version>` property in `src/Fetch/Fetch.csproj`.

## Unreleased

### Security
- Plain-HTTP package sources are refused. A downloaded package becomes code that Revit loads, so anyone able to modify the response could run code in every Revit session. Use `https://`, or set `AllowInsecureHttp=true` to accept the risk; `http://localhost` is still allowed.
- Downloads can be pinned with an expected `Sha256` in the settings; a mismatch aborts the sync before anything is installed.
- Per-user settings now take precedence over the machine-wide ones under `ProgramData`. The default permissions there let any standard user create files, so a machine-wide settings file must not override the user's own.
- The installer removes leftover machine-wide installs from releases before 1.0.3, which would otherwise load a second copy of FETCH and shadow per-user settings.

### Fixed
- **A failed sync no longer destroys your packages.** Content is staged and swapped into place only after it copies successfully, so an interrupted or failed sync leaves the existing packages untouched. Previously the folder was emptied first, and any failure after that point left it empty or half-populated.
- Deployed graph folders are backed up to `<folder>.fetch-backup` before being replaced, so edits made in place are recoverable.
- A source that does not look like a Dynamo package folder is refused instead of being copied over the packages folder and reported as success.
- Archives nested inside several wrapper folders (from re-zipping a deployment) are now found.
- Read-only files in a source no longer make every subsequent sync fail part-way through.
- A download that returns a web page — a sharing page, or a sign-in prompt — is reported as such instead of failing with a zip error.
- A stalled download is abandoned after 100 seconds without progress instead of hanging indefinitely. Slow but progressing downloads are no longer cut off.
- Syncing is skipped, with an explanation, when another Revit session has Dynamo open and its package files are in use. This check existed but its result was never used.
- Inspecting other Revit processes can no longer throw out of add-in startup.
- Two Revit sessions starting at once no longer race each other over the same packages folder.
- Revit 2025.4 (Dynamo 3.4) is recognised. A folder created by an earlier FETCH sync no longer outranks the folder Dynamo actually uses.
- The graph root from the settings is validated, so it cannot point a recursive replacement at a drive root or a well-known user folder.

### Changed
- The sync runs in the background instead of blocking Revit startup. Opening Dynamo within a few seconds of launching Revit may briefly show the previous packages.
- A successful sync is silent. Only problems you can act on raise a dialog, once, after Revit is idle.
- Starting Revit with no network access is treated as normal: the installed packages are left alone and nothing interrupts you.
- Every run appends to `FetchLog.txt` beside the installed add-in, recording what was resolved, what changed, and why anything failed.
- The connectivity check against `google.com` was removed; it answered a different question than whether the package source was reachable.

### Build
- Revit API package versions are pinned instead of floating, so a tag rebuilt later produces the same binaries.
- The release version is maintained in one place and verified against the release tag by CI.
- CI runs the test suite and fails on vulnerable dependencies before building a release.
- Builds no longer publish into the developer's live Revit add-ins folder unless asked; see `CONTRIBUTING.md`.
- Build outputs are no longer committed to the repository.
- Uninstalling removes the settings and log files it created.

## 1.0.4 and earlier

See the [GitHub releases](https://github.com/johnpierson/FETCH/releases) for the history before this changelog was started.
