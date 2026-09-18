# Building

Requires the .NET 8 SDK and Python 3. The mod targets .NET 6; tests run on .NET 8.
Launch Schedule I with MelonLoader once to generate its IL2CPP assemblies.

Set the MelonLoader path to the directory containing both `net6` and
`Il2CppAssemblies`. These game and loader dependencies stay on your machine.

## Windows (PowerShell)

```powershell
$loader = "C:\path\to\profile\MelonLoader"
dotnet build CoordinatedPolice.csproj --configuration Release "-p:MelonLoaderDir=$loader"
dotnet run --project tests/Tests.csproj
python package.py
```

## Linux / Nix

```sh
MELONLOADER_DIR='/path/to/profile/MelonLoader' ./build.sh
nix-shell -p dotnet-sdk_8 --run 'dotnet run --project tests/Tests.csproj'
```

The DLL is written to `bin/Release/net6.0/`. Packaging produces a Thunderstore ZIP
in `dist/`; neither directory belongs in source control.

## Formatting

Set `MelonLoaderDir` as an environment variable when formatting the main project:
`MelonLoaderDir=/path/to/profile/MelonLoader dotnet format CoordinatedPolice.csproj`
on Linux, or set `$env:MelonLoaderDir` in PowerShell before running the formatter.
Run `dotnet format tests/Tests.csproj` for tests and `ruff format package.py`
for the packaging script.

See [development notes](DEVELOPMENT.md) for implementation details and in-game checks.

## GitHub Actions

`Build and package` runs standalone tests and release-metadata checks on pull requests.
Pushes to main, `v*` tags and manual runs additionally build a Release DLL and produce a
validated Thunderstore ZIP. Download the `thunderstore-package` artifact from the run,
extract GitHub's artifact wrapper, then upload the mod ZIP inside to Thunderstore.
Artifacts expire after 30 days. CI does not run the game or prove multiplayer behaviour.

### Private references

GitHub-hosted CI checks out `holyfurries/schedule-i-build-references` using a fine-grained
GitHub token with Contents: read-only access to that repository. The private repository contains only a reference DLL bundle and its
build metadata. Public repos never contain the reference DLLs or the token.

These Actions settings are configured independently in all three mod repositories:

| Setting | Type | Meaning |
| --- | --- | --- |
| `REFERENCE_REPOSITORY` | Variable | Private owner/repository name |
| `REFERENCE_COMMIT` | Variable | Exact 40-character private repository commit |
| `REFERENCE_SHA256` | Variable | SHA256 of references.zip |
| `REFERENCE_TOKEN` | Secret | Fine-grained token with Contents: read-only access to the private repository |

Regenerate references after updating the game or MelonLoader, using Python 3.11+:

```sh
python ci/references.py pack /path/to/MelonLoader /private/repository/references.zip
```

The bundle includes only DLLs directly under `net6` and `Il2CppAssemblies`, excluding
profiles, logs, settings, saves and personal paths. Update the private build metadata,
commit and push the bundle, then set `REFERENCE_COMMIT` and `REFERENCE_SHA256` in all
three mod repositories. Keep the repository private. Never upload this bundle to
Thunderstore or a public release.

Create the token with holyfurries as resource owner and only schedule-i-build-references
selected. Add it as REFERENCE_TOKEN in each mod repository. Deploy keys are disabled
by the GitHub repository policy.

Pull requests never receive the token or references. Trusted builds check the checksum,
archive paths and size before extraction. Checkouts do not persist credentials; both
the private checkout and extracted references are removed after the build. Artifacts
contain only the explicitly named mod ZIP. The token should have no write permissions or access
to other private repositories. Rotate it by replacing the REFERENCE_TOKEN secret in
all three mod repositories before its expiry.

### Optional Thunderstore publishing

Create a service account in Thunderstore under **Settings → Teams → holyfurries →
Service Accounts**. Store its token as the GitHub Actions secret `TCLI_AUTH_TOKEN`
(repo secret, or in the `thunderstore` environment). Never commit the token.

1. Update the version in the project, package manifest and MelonInfo attribute together.
2. Commit/push the release, then create and push a matching `vX.Y.Z` tag.
3. Run **Build and package** on that tag with **Publish this version tag to Thunderstore**
   checked. The workflow must first be present on the default branch for manual runs.
   The GitHub CLI equivalent is:

   ```sh
   gh workflow run build.yml --ref vX.Y.Z -f publish=true
   ```

The workflow tests and builds the selected tag, retains its ZIP as an artifact, then
publishes that exact artifact using official `tcli` 0.2.4 to `holyfurries` / `schedule-i`.
Ordinary pushes, tag pushes and unchecked manual runs do not publish. A tag/version
mismatch fails before downloading references. Existing Thunderstore versions cannot be
replaced: bump the version for a new upload. If publishing fails after upload, inspect
Thunderstore before retrying. A workflow rerun is not a new version.

Setup links: [Thunderstore CLI authentication](https://github.com/thunderstore-io/thunderstore-cli/wiki#authentication)
and [repository Actions secrets](https://docs.github.com/en/actions/security-for-github-actions/security-guides/using-secrets-in-github-actions).
