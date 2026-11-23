# Visual Basic API reference

The [managed .NET API reference](../CSharp/README.md) defines the public types, parameters, defaults, errors and concurrency rules used from Visual Basic.

| Type or operation | Use from Visual Basic |
| --- | --- |
| `VesselStorage` | Construct it with `New VesselStorage(path)` inside `Using`. |
| `PutAsync` and `DeleteAsync` | Await their results inside an `Async Function`. Use named arguments such as `overwrite:=True` when needed. |
| `GetStream` | The caller owns the returned stream. Dispose it with `Using`. |
| `GetMetadata` and `GetAllKeys` | Check `HasValue` on the nullable metadata before accessing `Value`. Enumerate keys with `For Each`. |

See the [Visual Basic user guide](../../UserGuide/VisualBasic/README.md) for setup and a runnable example.
