"""Build and test a Python wheel with the native library produced by CI."""

import argparse
import os
import shutil
import subprocess
from pathlib import Path
from tempfile import TemporaryDirectory
from venv import EnvBuilder
from zipfile import ZipFile


PLATFORM_TAGS = {
    "win-x64": "win_amd64",
    "win-arm64": "win_arm64",
    "linux-x64": "manylinux_2_39_x86_64",
    "linux-arm64": "manylinux_2_39_aarch64",
    "linux-musl-x64": "musllinux_1_2_x86_64",
    "linux-musl-arm64": "musllinux_1_2_aarch64",
    "osx-x64": "macosx_15_0_x86_64",
    "osx-arm64": "macosx_15_0_arm64",
}


def library_name(rid: str) -> str:
    if rid.startswith("win-"):
        return "vessel.dll"
    if rid.startswith("osx-"):
        return "libvessel.dylib"
    return "libvessel.so"


def make_python(path: Path) -> Path:
    EnvBuilder(with_pip=True).create(path)
    return path / ("Scripts/python.exe" if os.name == "nt" else "bin/python")


def test_wheel(wheel: Path, library: str, tests: Path, work: Path) -> None:
    with ZipFile(wheel) as archive:
        if f"vessel/_lib/{library}" not in archive.namelist():
            raise ValueError(f"{wheel.name} is missing {library}")

    python = make_python(work / "test-env")
    env = os.environ.copy()
    env.pop("VESSEL_LIBRARY_PATH", None)
    env.pop("PYTHONPATH", None)
    subprocess.run(
        [python, "-m", "pip", "install", "--no-index", "--no-deps", wheel],
        cwd=work, env=env, check=True,
    )
    subprocess.run(
        [python, "-m", "unittest", "discover", "-s", tests],
        cwd=work, env=env, check=True,
    )


def build_wheel(rid: str, binary: Path, destination: Path) -> None:
    source = Path(__file__).resolve().parents[2] / "Bindings/python"
    tag = PLATFORM_TAGS[rid]
    dependencies = ["build", "setuptools>=77"]
    if rid.startswith("linux-"):
        dependencies.append("auditwheel>=6.6")
    elif rid.startswith("osx-"):
        dependencies.append("delocate")

    with TemporaryDirectory(prefix="vessel-wheel-") as directory:
        work = Path(directory)
        package = work / "package"
        shutil.copytree(
            source, package,
            ignore=shutil.ignore_patterns(".venv", "build", "dist", "*.egg-info", "__pycache__", "_lib"),
        )
        bundle = package / "src/vessel/_lib"
        bundle.mkdir()
        shutil.copy2(binary, bundle / binary.name)

        python = make_python(work / "build-env")
        subprocess.run([python, "-m", "pip", "install", *dependencies], check=True)
        env = dict(os.environ, VESSEL_WHEEL_PLATFORM=tag)
        raw = work / "raw"
        subprocess.run(
            [python, "-m", "build", "--wheel", "--no-isolation", "--outdir", raw, package],
            env=env, check=True,
        )
        (wheel,) = raw.glob("*.whl")
        repaired = work / "repaired"
        if rid.startswith("linux-"):
            subprocess.run(
                [python, "-m", "auditwheel", "repair", "--only-plat",
                 "--plat", tag, "--wheel-dir", repaired, wheel],
                check=True,
            )
            (wheel,) = repaired.glob("*.whl")
        elif rid.startswith("osx-"):
            arch = "arm64" if rid.endswith("arm64") else "x86_64"
            subprocess.run(
                [python, "-m", "delocate.cmd.delocate_wheel", "--require-archs", arch,
                 "--wheel-dir", repaired, wheel],
                env=dict(os.environ, MACOSX_DEPLOYMENT_TARGET="15.0"), check=True,
            )
            (wheel,) = repaired.glob("*.whl")

        if not wheel.name.endswith(f"-py3-none-{tag}.whl"):
            raise ValueError(f"Unexpected compatibility tag in {wheel.name}")
        test_wheel(wheel, binary.name, source / "tests", work)
        destination.mkdir(parents=True, exist_ok=True)
        print(shutil.copy2(wheel, destination / wheel.name))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", choices=PLATFORM_TAGS)
    parser.add_argument("library", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.library.name != library_name(args.rid) or not args.library.is_file():
        parser.error(f"Expected a {library_name(args.rid)} file for {args.rid}")
    build_wheel(args.rid, args.library.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
