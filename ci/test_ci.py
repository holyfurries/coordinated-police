import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

import references


class BuildChecks(unittest.TestCase):
    def test_reference_allowlist(self) -> None:
        for extra in [
            "../secret.dll",
            "net6/token.txt",
            "/net6/secret.dll",
            "net6/nested/file.dll",
            "UserData/private.dll",
        ]:
            with self.subTest(extra=extra), ZipFile(io.BytesIO(), "w") as archive:
                for name in references.REQUIRED:
                    archive.writestr(name, b"reference")
                archive.writestr(extra, b"bad")
                with self.assertRaises(ValueError):
                    references.validate(archive)
        with ZipFile(io.BytesIO(), "w") as archive:
            for name in references.REQUIRED:
                archive.writestr(name, b"reference")
            references.validate(archive)

    def test_reference_checksum(self) -> None:
        import hashlib

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bundle = root / "references.zip"
            with ZipFile(bundle, "w") as archive:
                for name in references.REQUIRED:
                    archive.writestr(name, b"reference")
            command = [
                sys.executable,
                str(Path(references.__file__)),
                "unpack",
                str(bundle),
                str(root / "unpacked"),
            ]
            environment = dict(os.environ, REFERENCE_SHA256="0" * 64)
            wrong = subprocess.run(
                command, env=environment, capture_output=True, check=False, timeout=10
            )
            self.assertNotEqual(wrong.returncode, 0)
            self.assertFalse((root / "unpacked").exists())
            environment["REFERENCE_SHA256"] = hashlib.sha256(
                bundle.read_bytes()
            ).hexdigest()
            good = subprocess.run(
                command, env=environment, capture_output=True, check=False, timeout=10
            )
            self.assertEqual(good.returncode, 0, good.stderr)
            self.assertTrue((root / "unpacked/net6/MelonLoader.dll").is_file())

    def test_release_guards(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for folder in ["ci", "src", "package", "dist"]:
                (root / folder).mkdir()
            shutil.copy(Path(__file__).with_name("release.py"), root / "ci/release.py")
            manifest = {
                "name": "TestMod",
                "version_number": "1.2.3",
                "description": "Test",
                "website_url": "https://example.com",
            }
            (root / "package/manifest.json").write_text(json.dumps(manifest))
            (root / "TestMod.csproj").write_text(
                "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>"
            )
            (root / "src/Main.cs").write_text(
                '[assembly: MelonInfo(typeof(Test.Main), "Test", "1.2.3", "Test")]'
            )
            environment = dict(
                os.environ, GITHUB_REF_TYPE="tag", GITHUB_REF_NAME="v1.2.3"
            )
            environment.pop("GITHUB_OUTPUT", None)

            def run(mode: str) -> int:
                return subprocess.run(
                    [sys.executable, str(root / "ci/release.py"), mode],
                    env=environment,
                    capture_output=True,
                    check=False,
                    timeout=10,
                ).returncode

            self.assertEqual(run("metadata"), 0)
            environment["GITHUB_REF_NAME"] = "v1.2.4"
            self.assertNotEqual(run("metadata"), 0)
            environment["GITHUB_REF_NAME"] = "v1.2.3"
            (root / "src/Main.cs").write_text(
                '[assembly: MelonInfo(typeof(Test.Main), "Test", "1.2.4", "Test")]'
            )
            self.assertNotEqual(run("metadata"), 0)
            (root / "src/Main.cs").write_text(
                '[assembly: MelonInfo(typeof(Test.Main), "Test", "1.2.3", "Test")]'
            )
            package = root / "dist/TestMod-1.2.3.zip"
            with ZipFile(package, "w") as archive:
                archive.writestr("manifest.json", json.dumps(manifest))
                for name in ["README.md", "LICENSE", "icon.png"]:
                    archive.writestr(name, b"test")
                archive.writestr("Mods/TestMod.dll", b"MZtest")
            self.assertEqual(run("package"), 0)
            with ZipFile(package, "a") as archive:
                archive.writestr("Assembly-CSharp.dll", b"private reference")
            self.assertNotEqual(run("package"), 0)


if __name__ == "__main__":
    unittest.main()
