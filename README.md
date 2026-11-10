# AccelerationAbsorbers

KSA mod to absorb high acceleration

A [Kitten Space Agency](https://kittenspaceagency.com) (KSA) code mod built on
[KSAModTools](https://github.com/AMPW-german/KSAModTools) and
[StarMap](https://github.com/StarMapLoader/StarMap).

## Layout

- `AccelerationAbsorbers/AccelerationAbsorbers.csproj` — the mod project (imports `tooling/msbuild/KsaMod.props` / `KsaMod.targets`)
- `AccelerationAbsorbers/Mod.cs` — `[StarMapMod]` entry point: applies Harmony patches and draws the welcome window
- `AccelerationAbsorbers/Patches.cs` — example Harmony prefix patch
- `tooling/` — KSAModTools submodule (MSBuild props/targets and CI actions)
- `.github/workflows/build.yml` — CI: builds, packages and releases the mod

## Build locally

Requires the .NET 10 SDK and the KSA game files — either a local install
(default `C:\Program Files\Kitten Space Agency`, override with `KSA_DIR` /
`KSADir`) or a KSADLLs snapshot via `KSA_DLL_REPO_DIR` with
`UseKsaNuGetPackages=true`. StarMap.API is referenced from a StarMap install
in your KSA mods folder (`StarMap/StarMap.API.dll`, override with `KSA_MODS_DIR`).

```sh
dotnet build -c Release
```

The build packages the mod into `Release/AccelerationAbsorbers/` and copies it into your
local KSA mods folder automatically.

## CI

`.github/workflows/build.yml` ships with automatic triggers disabled
(`workflow_dispatch` only) so it does nothing until secrets are configured.
To enable it, uncomment the `push`/`pull_request` triggers and set up:

| Secret / variable | Purpose |
| --- | --- |
| `KSADLLRepo` (secret) | `owner/repo` of a private KSA DLL snapshot repo (game DLLs + `ksa-versions.csv` + `KsaNuGet.props`; KSAModTools' `scripts/update_ksa_dlls.py` generates one) |
| `KSA_DLL_TOKEN` (secret) | PAT with read access to that repo — simpler alternative to the App below |
| `APP_ID` + `APP_PRIVATE_KEY` (secrets) | GitHub App credentials for the DLL repo, alternative to `KSA_DLL_TOKEN` |
| `SPACEDOCKUSERNAME` (variable) | your SpaceDock username |
| `SPACEDOCKAUTH` (secret) | your SpaceDock API token |
| `SPACEDOCK_ID` | set in `build.yml` — your mod's SpaceDock ID |

On pushes to `main` that change `VersionPrefix`, the workflow also publishes a
GitHub release and uploads the package to SpaceDock.

Generated from [KSAModTemplate](https://github.com/AMPW-german/KSAModTemplate).
