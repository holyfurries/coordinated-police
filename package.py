import json
import struct
import zlib
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


def main() -> None:
    root = Path(__file__).resolve().parent
    manifest = json.loads((root / "package/manifest.json").read_text())
    assert len(manifest["description"]) <= 250
    assembly = root / "bin/Release/net6.0/CoordinatedPolice.dll"
    if not assembly.is_file():
        raise FileNotFoundError("Build CoordinatedPolice.dll before packaging")
    pixels = bytearray()
    for y in range(256):
        pixels.append(0)
        for x in range(256):
            shield = 48 <= x < 208 and 36 <= y < 156 + (80 - abs(x - 128)) // 2
            stripe = shield and (96 <= x < 112 or 144 <= x < 160) and 72 <= y < 156
            pixels.extend(
                (239, 244, 250)
                if stripe
                else (40, 104, 178)
                if shield
                else (17, 24, 39)
            )
    icon = b"\x89PNG\r\n\x1a\n"
    for kind, data in (
        (b"IHDR", struct.pack(">2I5B", 256, 256, 8, 2, 0, 0, 0)),
        (b"IDAT", zlib.compress(bytes(pixels))),
        (b"IEND", b""),
    ):
        icon += (
            struct.pack(">I", len(data))
            + kind
            + data
            + struct.pack(">I", zlib.crc32(kind + data))
        )
    target = root / "dist" / f"CoordinatedPolice-{manifest['version_number']}.zip"
    target.parent.mkdir(exist_ok=True)
    with ZipFile(target, "w", ZIP_DEFLATED) as archive:
        archive.write(root / "package/manifest.json", "manifest.json")
        archive.write(root / "README.md", "README.md")
        archive.write(root / "LICENSE", "LICENSE")
        archive.writestr("icon.png", icon)
        archive.write(assembly, "Mods/CoordinatedPolice.dll")
    with ZipFile(target) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == {
            "manifest.json",
            "README.md",
            "LICENSE",
            "icon.png",
            "Mods/CoordinatedPolice.dll",
        }
    print(target)


if __name__ == "__main__":
    main()
