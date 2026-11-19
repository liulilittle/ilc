# System.Convert.ToDateTime and IsDBNull

Status: GREEN (`probe-convdt`, byte-exact against the net48 oracle, sha
`0304fe5bfd0cb46bd1b9ac5ba1bce49964ac08ddfdd31f3540c0ef244b633320`, 846B).

## Net48 measurements

Every rule below was measured on the net48 oracle, not inferred:

| Behaviour | net48 |
|---|---|
| `ToDateTime(string)` | `DateTime.Parse` over the invariant subset |
| `ToDateTime(null)` | `DateTime.MinValue` (0001-01-01), NOT an exception |
| `ToDateTime(string, IFormatProvider)` | same, provider dropped |
| `ToDateTime(DateTime)` | identity |
| `ToDateTime(object)` null | `DateTime.MinValue` |
| `ToDateTime(object)` string | `Parse` |
| `ToDateTime(object)` other / DBNull | InvalidCastException |
| `ToDateTime(bool/byte/char/decimal/double/short/int/long/sbyte/float/ushort/uint/ulong)` | InvalidCastException |
| `IsDBNull` | true only for the shared DBNull sentinel (null -> false) |

## Implementation shape

* `icall_convert_to_datetime_string` answers a null string with
  `Il2CppDateTime(0, 0)` itself.  `il2cpp_datetime_parse` throws
  `ArgumentNullException` on null, so the Convert null-means-default rule must
  be applied by the caller -- the Convert layer is not `DateTime.Parse`.
* `icall_convert_to_datetime_datetime` is the identity.
* `icall_convert_to_datetime_object` dispatches on the runtime type: null ->
  MinValue, a string -> Parse, anything else -> InvalidCastException.
* `icall_convert_to_datetime_cast` always throws InvalidCastException (the
  numeric sources).
* `icall_convert_is_dbnull` delegates to `il2cpp_datatable_is_dbnull`.

## Two traps the probe caught

1. `0x11` (VALUETYPE) is shared by `DateTime` and `Decimal`.  The identity arm
   must match the tracked type name (`V:System.DateTime` / `System.DateTime`);
   an untracked `0x11` stays on the InvalidCastException arm.
2. A dropped provider argument means the icall keeps ONE parameter
   (`ParamCount = 1` with `"drop"`), not two -- otherwise the emitter generates
   a two-argument call to a one-parameter function.

## Mutation evidence

Removing the null special-case in `icall_convert_to_datetime_string` turns the
probe RED at stdout record 141 (`ArgumentNullException` instead of MinValue);
restoring it returns the sha above.

## Still unmapped

`ChangeType` (4 overloads), `GetTypeCode` and `ToDateTime`'s remaining
`IFormatProvider` shapes need `System.Type` / `TypeCode` /
`IFormatProvider` support beyond what the probe pins.
