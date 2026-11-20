"""Turn the CI's tested native artifacts into flat archives for a tagged release."""

import argparse
import shutil
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from packaging.utils import parse_wheel_filename
from packaging.version import Version

from build_python_wheel import PLATFORM_TAGS, library_name


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    parser.add_argument("release", type=Path)
    parser.add_argument("version")
    args = parser.parse_args()

    for rid, platform_tag in PLATFORM_TAGS.items():
        artifact = args.artifacts / f"vessel-wheel-{rid}-{args.version}"
        wheels = list(artifact.glob("*.whl"))
        if len(wheels) != 1:
            parser.error(f"Expected one Python wheel in {artifact.name}")
        wheel = wheels[0]
        name, version, _, tags = parse_wheel_filename(wheel.name)
        if (name != "vessel-storage" or version != Version(args.version) or
                {str(tag) for tag in tags} != {f"py3-none-{platform_tag}"}):
            parser.error(f"Unexpected Python wheel for {rid}: {wheel.name}")
        with ZipFile(wheel) as archive:
            if f"vessel/_lib/{library_name(rid)}" not in archive.namelist():
                parser.error(f"{wheel.name} is missing {library_name(rid)}")
        shutil.copy2(wheel, args.release / wheel.name)

    dynamic_files = {
        "windows": ("vessel.dll", "vessel.lib"),
        "linux": ("libvessel.so",),
        "macos": ("libvessel.dylib",),
    }
    dynamic_artifacts = sorted(args.artifacts.glob(f"vessel-native-dynamic-*-{args.version}"))
    if len(dynamic_artifacts) != len(PLATFORM_TAGS):
        parser.error(f"Expected {len(PLATFORM_TAGS)} dynamic library artifacts")
    static_files = {
        f"vessel-native-static-windows-x64-{args.version}": "vessel_static.lib",
        f"vessel-native-static-linux-glibc-x64-{args.version}": "libvessel.a",
    }

    for artifact in [*dynamic_artifacts, *(args.artifacts / name for name in static_files)]:
        if artifact.name in static_files:
            binaries = (
                static_files[artifact.name],
                "vessel-link.rsp",
                "DOTNET-LICENSE.txt",
                "DOTNET-THIRD-PARTY-NOTICES.txt",
            )
        else:
            platform = artifact.name.removeprefix("vessel-native-dynamic-").split("-", 1)[0]
            binaries = dynamic_files[platform]
        files = (*binaries, "vessel.h", "LICENSE")
        for file in files:
            if not (artifact / file).is_file():
                parser.error(f"Missing {file} from {artifact.name}")

        destination = args.release / f"{artifact.name}.zip"
        with ZipFile(destination, "w", compression=ZIP_DEFLATED) as archive:
            for file in files:
                archive.write(artifact / file, arcname=file)
        print(destination.name)


if __name__ == "__main__":
    main()
