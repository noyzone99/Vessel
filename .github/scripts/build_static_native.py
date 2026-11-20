"""Bundle the NativeAOT static library and verify it with the C smoke client."""

import argparse
import shutil
import subprocess
from collections import defaultdict
from pathlib import Path
from tempfile import TemporaryDirectory


def archive_for_linux(archive: Path, inputs: list[str]) -> None:
    paths = [Path(filename) for filename in inputs]
    managed = paths.pop(0)
    bootstrapper = next(path for path in paths if path.name == "libbootstrapperdll.o")
    paths.remove(bootstrapper)
    entry = archive.parent / "vessel.o"
    # Keep the bootstrapper's ELF initializer in the object that supplies the C API.
    subprocess.run(["ld", "-r", "-o", entry, managed, bootstrapper], check=True)

    # MRI commands use local filenames so paths with spaces also work.
    members = []
    for number, path in enumerate([entry, *paths]):
        member = archive.parent / f"input-{number}{path.suffix}"
        member.symlink_to(path)
        command = "ADDLIB" if path.suffix == ".a" else "ADDMOD"
        members.append(f"{command} {member.name}")
    instructions = "\n".join([f"CREATE {archive.name}", *members, "SAVE", "END", ""])
    subprocess.run(["ar", "-M"], input=instructions, text=True, cwd=archive.parent, check=True)


def link_and_test(root: Path, work: Path, package: Path, rid: str, inputs: dict[str, list[str]]) -> None:
    source = root / "Tests/Vessel.Native.Tests/smoke.c"
    response = package / "vessel-link.rsp"
    if rid == "win-x64":
        archive = package / "vessel_static.lib"
        subprocess.run(["lib.exe", "/nologo", f"/OUT:{archive}", *inputs["archive"]], check=True)
        response.write_text("\n".join([
            # Pull in the NativeAOT bootstrapper and its CRT initializer.
            "/INCLUDE:__modules_a",
            "/MERGE:.managedcode=.text",
            "/MERGE:hydrated=.bss",
            "/NODEFAULTLIB:libucrt.lib",
            "/DEFAULTLIB:ucrt.lib",
            *inputs["system"],
        ]) + "\n", encoding="ascii")
        executable = work / "smoke.exe"
        smoke_object = work / "smoke.obj"
        subprocess.run([
            "cl.exe", "/nologo", "/c", "/MT", "/std:c11", "/W4", "/WX", f"/I{package}",
            f"/Fo{smoke_object}", source,
        ], cwd=work, check=True)
        subprocess.run([
            "link.exe", "/nologo", f"/OUT:{executable}", smoke_object, archive, f"@{response}",
        ], cwd=work, check=True)
        dependencies = subprocess.check_output(["dumpbin.exe", "/DEPENDENTS", executable], text=True)
        if "vessel.dll" in dependencies.lower():
            raise RuntimeError("The C smoke executable imports vessel.dll")
    else:
        archive = package / "libvessel.a"
        archive_dir = work / "archive"
        archive_dir.mkdir()
        archive_for_linux(archive_dir / archive.name, inputs["archive"])
        shutil.move(archive_dir / archive.name, archive)
        response.write_text("\n".join([
            "-pthread",
            *(f"-l{library}" for library in inputs["system"]),
        ]) + "\n", encoding="ascii")
        executable = work / "smoke"
        subprocess.run([
            "clang", "-std=c11", "-Wall", "-Wextra", "-Werror", "-I", package, source,
            archive, f"@{response}", "-o", executable,
        ], cwd=work, check=True)
        dependencies = subprocess.check_output(["readelf", "-d", executable], text=True)
        if "libvessel.so" in dependencies:
            raise RuntimeError("The C smoke executable depends on libvessel.so")

    subprocess.run([executable, work / "store"], cwd=work, check=True)


def build(rid: str, output: Path) -> None:
    root = Path(__file__).resolve().parents[2]
    with TemporaryDirectory(prefix="vessel-static-") as directory:
        work = Path(directory)
        publish = work / "publish"
        manifest = work / "link-inputs.txt"
        subprocess.run([
            "dotnet", "publish", "Sources/Vessel.Native", "-c", "Release", "-r", rid,
            "--artifacts-path", work / "build", "-o", publish,
            "-p:NativeLib=Static", "-p:StripSymbols=false", f"-p:VesselStaticLinkInputs={manifest}",
        ], cwd=root, check=True)

        inputs = defaultdict(list)
        for line in manifest.read_text(encoding="utf-8-sig").splitlines():
            kind, path = line.split("|", 1)
            inputs[kind].append(path)

        package = work / "package"
        package.mkdir()
        shutil.copy2(publish / "vessel.h", package)
        shutil.copy2(root / "LICENSE", package)
        (runtime,) = inputs["runtime"]
        runtime_package = Path(runtime).resolve().parents[2]
        shutil.copy2(runtime_package / "LICENSE.TXT", package / "DOTNET-LICENSE.txt")
        shutil.copy2(
            runtime_package / "THIRD-PARTY-NOTICES.TXT", package / "DOTNET-THIRD-PARTY-NOTICES.txt",
        )
        link_and_test(root, work, package, rid, inputs)
        output.mkdir(parents=True, exist_ok=True)
        for file in package.iterdir():
            shutil.copy2(file, output)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", choices=("win-x64", "linux-x64"))
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build(args.rid, args.output.resolve())


if __name__ == "__main__":
    main()
