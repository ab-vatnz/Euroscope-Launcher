# EuroScope Launcher

Windows launcher for the VATNZ EuroScope package. It manages a single shared `AIRAC` folder at the base of the EuroScope installation, checks for current VATNZ sector files, installs supported plugins, and starts EuroScope.

## Install and first setup

1. Download `EuroScopeLauncher-Setup.exe` from this repository's GitHub Releases and run it. It installs the app and creates **EuroScope Launcher** shortcuts on the Desktop and Start menu.
2. Start **EuroScope Launcher**. It defaults to `C:\Program Files (x86)\EuroScope\EuroScope.exe`; browse to the executable if yours differs.
3. If a `VATNZ-SKYLINE_*` folder is detected, the first setup click creates an empty `AIRAC` folder and stops. Back up the old package, then move or copy its `Settings` folder and any controller-specific files you want to retain into `C:\Program Files (x86)\EuroScope\AIRAC`. Run setup again afterwards. The launcher never deletes the old package or replaces/updates an existing `AIRAC\Settings` folder.
4. Select **First-time SkyLine setup**. For an empty AIRAC folder it downloads the current VATNZ SkyLine package and asks whether to copy its `VATNZ.prf`. If you have already migrated files into AIRAC, it preserves them and installs only the current SCT2, ESE, and RWY files.
5. In EuroScope, select the SCT2 file from `AIRAC` before connecting.

EuroScope Launcher always requests administrator permission when it starts. This allows AIRAC and plugin updates under `Program Files` to work reliably.

## AIRAC updates

The launcher reads the current package from [VATNZ Sector Files](https://www.vatnz.net/airspace/sector_files/). During initial SkyLine setup, an existing `AIRAC\Settings` folder is retained. After initial setup it only replaces `.sct2`, `.ese`, and `.rwy` files in `AIRAC`; it never alters `.prf`, plugin, settings, or other manually migrated files. `euroscopelauncher-airac.txt` beside `AIRAC` records the installed version and source.

## Plugins

The versioned [plugin catalog](plugin-catalog.json) controls the available plugins. Each entry declares its GitHub release API, ZIP-asset matcher, destination, primary DLL, and post-install guidance. The launcher lists installed and latest release versions, and has explicit **Install**, **Update**, and confirmed **Uninstall** actions. The starter entry installs the complete OzStrips EuroScope release under `AIRAC\Plugins\OzStripsEuroScope`.

After installing a plugin, open **Other SET → Plug-ins** in EuroScope, load the displayed DLL, and enable it. For OzStrips, connect and enter `.ozstrips`.

## Releasing

Push a `v*` tag to create a GitHub Release. The workflow self-contains the .NET launcher, builds `EuroScopeLauncher-Setup.exe` with Inno Setup, and uploads it. Configure certificate signing separately in GitHub Actions if a code-signing certificate becomes available; no certificate is stored in this repository.

Use `main` for releases and plain branch names such as `feature-airac-update`; do not use branch names containing `/codex`.
