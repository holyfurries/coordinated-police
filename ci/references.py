import argparse
import hashlib
import os
import re
import shutil
from pathlib import Path, PurePosixPath
from zipfile import ZIP_DEFLATED, ZipFile

LIMIT = 512 * 1024 * 1024
REQUIRED = {
    "net6/MelonLoader.dll",
    "net6/0Harmony.dll",
    "net6/Il2CppInterop.Runtime.dll",
    "Il2CppAssemblies/Assembly-CSharp.dll",
}


def validate(archive: ZipFile) -> None:
    entries = archive.infolist()
    if len(entries) > 1024 or sum(entry.file_size for entry in entries) > LIMIT:
        raise ValueError("Reference bundle exceeds limits")
    names = set()
    for entry in entries:
        path = PurePosixPath(entry.filename)
        if len(path.parts) != 2 or path.parts[0] not in {"net6", "Il2CppAssemblies"}:
            raise ValueError("Unexpected reference bundle path")
        if (
            not re.fullmatch(r"[A-Za-z0-9_.+-]+\.dll", path.name)
            or entry.filename in names
        ):
            raise ValueError("Invalid or duplicate reference filename")
        names.add(entry.filename)
    if not REQUIRED <= names:
        raise ValueError("Reference bundle is missing required assemblies")


def main() -> None:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    pack = commands.add_parser("pack")
    pack.add_argument("loader", type=Path)
    pack.add_argument("output", type=Path)
    unpack = commands.add_parser("unpack")
    unpack.add_argument("bundle", type=Path)
    unpack.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.command == "pack":
        files = sorted(
            path
            for folder in ("net6", "Il2CppAssemblies")
            for path in (args.loader / folder).glob("*.dll")
        )
        if len(files) > 1024 or sum(path.stat().st_size for path in files) > LIMIT:
            raise ValueError("References exceed limits")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(args.output, "w", ZIP_DEFLATED) as archive:
            for path in files:
                archive.write(path, f"{path.parent.name}/{path.name}")
        with ZipFile(args.output) as archive:
            validate(archive)
        with args.output.open("rb") as source:
            print(f"SHA256={hashlib.file_digest(source, 'sha256').hexdigest()}")
        return
    digest = os.getenv("REFERENCE_SHA256", "")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise ValueError("Set the REFERENCE_SHA256 repository variable")
    if args.bundle.stat().st_size > LIMIT:
        raise ValueError("Reference bundle exceeds size limit")
    with args.bundle.open("rb") as source:
        if hashlib.file_digest(source, "sha256").hexdigest() != digest.lower():
            raise ValueError("Reference bundle SHA256 mismatch")
    with ZipFile(args.bundle) as archive:
        validate(archive)
        args.output.mkdir(parents=True, exist_ok=False)
        for entry in archive.infolist():
            target = args.output / entry.filename
            target.parent.mkdir(exist_ok=True)
            with archive.open(entry) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination, length=1024 * 1024)


if __name__ == "__main__":
    main()
