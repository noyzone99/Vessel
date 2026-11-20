"""Stage the tested native artifacts and check their exact contents in the Java JAR."""

import argparse
import shutil
from pathlib import Path
from zipfile import ZipFile

from build_python_wheel import PLATFORM_TAGS, library_name


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    parser.add_argument("version")
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--stage", type=Path)
    modes.add_argument("--check", type=Path)
    args = parser.parse_args()

    natives = {}
    for rid in PLATFORM_TAGS:
        platform, arch = rid.rsplit("-", 1)
        target = {"win": "windows", "osx": "macos", "linux": "linux-glibc"}.get(platform, platform)
        resource = f"{rid}/{library_name(rid)}"
        binary = args.artifacts / f"vessel-native-dynamic-{target}-{arch}-{args.version}" / library_name(rid)
        if not binary.is_file() or binary.stat().st_size == 0:
            parser.error(f"Missing native library: {binary}")
        natives[resource] = binary

    if args.stage is not None:
        for resource, binary in natives.items():
            path = args.stage / resource
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(binary, path)
        return

    prefix = "META-INF/vessel/native/"
    with ZipFile(args.check) as jar:
        resources = [name.removeprefix(prefix) for name in jar.namelist()
                     if name.startswith(prefix) and not name.endswith("/")]
        if sorted(resources) != sorted(natives):
            parser.error(f"{args.check.name} has unexpected native resources: {resources}")
        for resource, binary in natives.items():
            if jar.read(prefix + resource) != binary.read_bytes():
                parser.error(f"{args.check.name} has the wrong native library for {resource}")


if __name__ == "__main__":
    main()
