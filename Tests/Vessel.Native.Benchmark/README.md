# Vessel C ABI benchmark

This program links to the NativeAOT shared library and calls the public C ABI. Use the same executable for baseline and candidate runs, with the matching library loaded at process startup. Record the DLL or shared object size, build configuration, runtime, CPU, OS, architecture, filesystem, storage device and cache conditions alongside the CSV output.

## Build

From an x64 Visual Studio Developer PowerShell, with the published header and the import library from the same build:

```powershell
$include = '<directory containing the published vessel.h>'
$library = '<path to native vessel.lib>'
cl.exe /nologo /std:c11 /O2 /W4 /WX "/I$include" Tests/Vessel.Native.Benchmark/benchmark.c $library /Fe:vessel-benchmark.exe
$env:PATH = '<directory containing vessel.dll>' + [IO.Path]::PathSeparator + $env:PATH
```

On Linux or macOS, with the published header and shared library in the same directory:

```sh
native_dir=/path/to/publish
cc -std=c11 -O2 -Wall -Wextra -Werror -pthread -I"$native_dir" \
  Tests/Vessel.Native.Benchmark/benchmark.c -L"$native_dir" -lvessel -o vessel-benchmark
```

Set `LD_LIBRARY_PATH` on Linux or `DYLD_LIBRARY_PATH` on macOS to the library directory when running the executable. On Windows, keep the executable outside directories that contain competing `vessel.dll` files.

## Run

Pass a fresh directory for each timed data run. Keep the directory to measure reopening separately.

```sh
./vessel-benchmark /path/to/empty-store read zstd 65536 8 128
./vessel-benchmark /path/to/another-empty-store put none 1024 8 128
./vessel-benchmark /path/to/another-empty-store reopen none 0 1 5
```

The final two arguments set worker count and operations per worker. Use `1` worker and `0` payload bytes for `reopen`. Payloads use a deterministic pseudorandom half and a repeated byte in the other half. All writes use the balanced codec level.

| Operation | Measured work |
| --- | --- |
| `read` | Opens, reads at most 64 KiB per ABI call through EOF, and closes a stream. Each worker reads its own seeded object. The setup reads each object once to warm the filesystem cache. |
| `put` | Calls `vessel_put_ex` without an output result for a unique key and payload. |
| `pull` | Calls `vessel_put_stream` without an output result for a unique key and payload. The C callback supplies the data from memory. |
| `overwrite` | Replaces one key per worker with unique payloads through `vessel_put_ex`. |
| `dedup` | Writes new keys through `vessel_put_ex` using the same pre-seeded payload. |
| `delete` | Deletes independent keys. The seeded objects share one blob, and the final delete removes it. |
| `reopen` | Times each `vessel_init` on an existing store. Dispose runs between samples and is excluded from the reported open time. |

The CSV reports wall-clock throughput, nearest-rank p50/p95/p99 and process memory in bytes. First-open timing includes the first ABI call and excludes executable and loader startup. Peak RSS covers the whole process, including fixture setup. Other memory columns are snapshots before and after the timed operations. `harness_payload_bytes` reports input fixtures still held during those operations. Windows private bytes refer to committed private memory. A zero memory column indicates that the platform does not provide that counter in this harness. Use a separate profiler for GC allocation, native heap attribution and lock or flush timings.
