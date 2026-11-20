# Python interop reference

Python code calls the [C ABI](../C/README.md) directly through `ctypes.CDLL`. Use the [`vessel.h`](../../../Sources/Vessel.Native/vessel.h) declarations from the same build as the native library, then check `vessel_abi_version()`. Set `argtypes` and `restype` before calling each function.

## Type mappings

| C declaration | `ctypes` type |
| --- | --- |
| `int32_t`, including status codes and enums | `c_int32` |
| `int64_t` lengths, offsets and timestamps | `c_int64` |
| `vessel_t`, `vessel_stream_t`, `void *` | `c_void_p` |
| `vessel_t *`, `vessel_stream_t *` outputs | `POINTER(c_void_p)` |
| NUL-terminated UTF-8 `char *` | `c_char_p` for text passed in or read as bytes |
| `uint8_t *` | `POINTER(c_uint8)` or `c_void_p` with an explicit byte count |
| C structs | `Structure` with the header's field order, widths and native alignment |
| `void` return | `restype = None` |

Match pointer and by-value arguments exactly. For example, `vessel_put` takes `vessel_object_info_t` by value and `vessel_read` takes a pointer to writable `vessel_object_t` storage. Use `byref()` for output structs and handles as shown in the [Python guide](../../UserGuide/Python/README.md).

## Callbacks and resource ownership

- Use `CFUNCTYPE` for C callbacks. For example, a streaming upload callback has the signature `CFUNCTYPE(c_int32, c_void_p, POINTER(c_uint8), c_int32)` for context, buffer and capacity. Keep strong references to callback objects and their context until the native operations finish. Return a C status or count from callbacks and handle Python exceptions inside them.
- Copy callback text or result data before the borrowed or owned memory expires. For whole reads, copy with `string_at(result.data, result.length)` and call `vessel_free_object(byref(result))`. For extended metadata, pass `byref(result.metadata)` to `vessel_free_metadata`.
- Call `vessel_stream_close` for stream handles and `vessel_dispose` for store handles. A `try`/`finally` block can release them even when a Python operation raises an exception.
- The [C ABI status table](../C/README.md#status-codes) defines failure codes. `ctypes` returns the integer status. Check it before reading output fields.

The [Python smoke test](../../../Tests/Vessel.Native.Tests/smoke.py) provides verified bindings for a subset of the native operations and demonstrates callback lifetimes, concurrent calls and recovery.
