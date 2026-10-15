# GAPS.md -- p-excvalidate recorded findings (GN-20 legacy MSCORLIB exception-type batch)

Case status: **PASS** (mingw, 24-record net48 oracle byte-exact; oracle sha ==
runtime sha 6ca0c0452a97572650ec5b3815d08ddf98581ef54fcb2c5d8aca5d7970d1c1a5).

Scope: the exception TYPE the default zh-CN net48 console process throws when a
String/StringBuilder API is called with an invalid argument (ArgumentNull-
Exception vs ArgumentOutOfRangeException vs FormatException vs no-throw). The
probe records `[id]=THREW:<TypeName>` or `[id]=OK` / an escaped content value;
localized Message text is captured separately (env-gated) and NEVER part of the
byte-exact gate.

## FIXED this batch (byte-exact, runtime icall + MethodMap domains)

Three P1/EXC-gap fixes, each red->green on the p-excvalidate oracle:

1. **`String.Concat((string[])null)` -- was silently returning ""** (the shared
   `ConcatFromArray` pipeline's defensive `array == nullptr -> EmptyString`).
   net48 measured: `Concat(string[])` / `Concat(object[])` BOTH throw
   ArgumentNullException on a null array. Fix: `il2cpp_string_concat_array`
   (concat.cpp, the `String.Concat` entry) now throws
   `il2cpp_throw_argument_null_exception("values")` before reaching the shared
   pipeline. (The 2/3/4-arg forms and element-null behavior are untouched.)

2. **`StringBuilder.Length` setter (`set_Length`) was NOT wired at all** --
   the fold skipped it ("BCL owner has no mapping arm"), so `sb.Length = x`
   silently no-op'd. net48 measured: value < 0 -> ArgumentOutOfRangeException;
   value > Length GROWS the buffer zero-filling the new units ("abc" -> 5 ->
   "abc\0\0"); value < Length truncates ("abc\0\0" -> 1 -> "a"). Fix: new
   runtime icall `icall_stringbuilder_set_length` (stringbuilder.cpp) +
   MethodMap.StringBuilder.cs `set_Length` arm (count 1, int32 value).

3. **`StringBuilder.ToString(int,int)` was mis-mapping to the 0-arg
   `icall_stringbuilder_to_string`** (a C0 build break: the two ints passed as
   the object receiver). net48 measured: startIndex<0 or length<0 or
   startIndex+length>Length -> ArgumentOutOfRangeException; otherwise the
   window string. Fix: MethodMap gates count==2 to a new
   `icall_stringbuilder_to_string_range` (stringbuilder.cpp, 64-bit-sum AOORE
   bounds).

## Verified-still-aligned (covered by the probe, no change needed)

- `String.Substring(start<0 / range over)` -> AOORE
- `String.Insert(index over)` -> AOORE; `Insert(idx, null)` -> ANE
- `String.Remove(start<0 / count over)` -> AOORE
- `String.IndexOf(null)` -> ANE; `IndexOf("b", -1)` -> AOORE
- `String.StartsWith(null)` -> ANE
- `String.TrimStart((char[])null)` -> NO THROW (whitespace trim)
- `String.Join(null separator, ...)` -> NO THROW (null sep == empty)
- `StringBuilder ctor(capacity<0)` -> AOORE
- `StringBuilder.Append(null)` -> NO THROW (no-op, returns the builder)
- `String.Format(null format)` -> ANE; `Format("{0} {3}")` -> FormatException

## W7 -- not aligned (honest white list)

1. **Exception MESSAGE text / parameter names are not aligned.** The diff gate
   is the exception TYPE (the catchable semantic contract). zh-CN net48
   emits localized messages ("值不能为 null。" etc.) that are host-dependent;
   the runtime emits English text. A host that branches on
   `ex.Message` (not `ex.GetType()`) would see different strings. Recorded W7:
   exception-type alignment only.
2. `StringBuilder.ToString(int,int)`/`set_Length` grow semantics beyond the
   probe's shape (value 0..small, buffer retention) are exercised only up to
   the probe extent; MaxCapacity-pinning (int.MaxValue) is unchanged (D5
   contract). `set_Length` beyond capacity (very large values) follows the
   existing GrowBuffer/OOM path, not separately probed.
3. `String.Concat(object[])` with a null array is served by the same
   `il2cpp_string_concat_array` (param name "values" used for both -- net48
   names the object[] overload's parameter "args"). Type-aligned (ANE) even
   where the parameter name differs.