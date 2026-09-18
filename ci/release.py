import argparse
import json
import os
import re
import xml.etree.ElementTree as xml
from pathlib import Path
from zipfile import ZipFile


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["metadata", "package"])
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / "package/manifest.json").read_text())
    name, version = manifest["name"], manifest["version_number"]
    if not re.fullmatch(r"[A-Za-z0-9_]+", name):
        raise ValueError("Invalid package name")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Expected a three-part release version")
    project = xml.parse(root / f"{name}.csproj").getroot()
    if project.findtext("PropertyGroup/Version") != version:
        raise ValueError("Project and manifest versions differ")
    assembly = (root / "src/Main.cs").read_text()
    melon = re.search(r'MelonInfo\(typeof\([^)]*\),\s*"[^"]*",\s*"([^"]+)"', assembly)
    if melon is None or melon[1] != version:
        raise ValueError("MelonInfo and manifest versions differ")
    if (
        os.getenv("GITHUB_REF_TYPE") == "tag"
        and os.getenv("GITHUB_REF_NAME") != f"v{version}"
    ):
        raise ValueError("Release tag must match v<manifest version>")
    filename = f"{name}-{version}.zip"
    if args.mode == "package":
        with ZipFile(root / "dist" / filename) as archive:
            expected = {
                "manifest.json",
                "README.md",
                "LICENSE",
                "icon.png",
                f"Mods/{name}.dll",
            }
            if (
                len(archive.infolist()) != len(expected)
                or set(archive.namelist()) != expected
            ):
                raise ValueError("Unexpected files in package")
            if archive.testzip() is not None:
                raise ValueError("Corrupt package")
            if json.loads(archive.read("manifest.json")) != manifest:
                raise ValueError("Packaged manifest differs from source")
            if not archive.read(f"Mods/{name}.dll").startswith(b"MZ"):
                raise ValueError(
                    "Package does not contain a Windows-format .NET assembly"
                )
        config = (
            '[config]\nschemaVersion = "0.0.1"\n\n[package]\nnamespace = "holyfurries"\n'
            f"name = {json.dumps(name)}\nversionNumber = {json.dumps(version)}\n"
            f"description = {json.dumps(manifest['description'])}\n"
            f"websiteUrl = {json.dumps(manifest['website_url'])}\ncontainsNsfwContent = false\n"
            '\n[build]\noutdir = "./dist"\n\n[publish]\nrepository = "https://thunderstore.io"\n'
            'communities = ["schedule-i"]\n\n[publish.categories]\nschedule-i = []\n'
        )
        (root / "dist/thunderstore.toml").write_text(config)
    output = f"name={name}\nversion={version}\nfilename={filename}\n"
    if os.getenv("GITHUB_OUTPUT"):
        with Path(os.environ["GITHUB_OUTPUT"]).open("a") as target:
            target.write(output)
    print(output, end="")


if __name__ == "__main__":
    main()
