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
