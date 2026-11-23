# F# API reference

The [managed .NET API reference](../CSharp/README.md) defines the public types, parameters, defaults, errors and concurrency rules used from F#.

| Type or operation | Use from F# |
| --- | --- |
| `VesselStorage` | Construct it with `new VesselStorage(path)` and keep it in a `use` binding. |
| `PutAsync` and `DeleteAsync` | Await their results with `let!` inside a `task` computation expression. Use named arguments such as `overwrite = true` when needed. |
| `GetStream` | The caller owns the returned stream. Dispose it with `use`. |
| `GetMetadata` and `GetAllKeys` | Check `HasValue` on the nullable metadata before accessing `Value`. Enumerate the returned keys as a sequence. |

See the [F# user guide](../../UserGuide/FSharp/README.md) for setup and a runnable example.
