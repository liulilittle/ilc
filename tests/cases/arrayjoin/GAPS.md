# GAPS.md -- arrayjoin recorded findings (e2e batch C84, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 41-line byte-exact).

## D-AJ-1 -- M3 user-method SZARRAY returns unsupported

- **Phenomenon**: same as gcweak2 D-WR-1 -- a user method returning a
  `string[]`/`WeakReference[]` throws "return type SZArray is not supported
  by M3". This case returns strings only; the joined results are consumed
  via Substring/Length/indexer reads in the same method.

## D-AJ-2 -- object[] Concat/Join element conversion lives in strjoin

- `string.Join(string, object[])` / `string.Concat(object[])` share the same
  runtime icalls (JoinFromArray/ConcatFromArray convert non-string elements
  via icall_object_to_string), but the mixed boxed-element surface is already
  driven by the strjoin case. arrayjoin stays on the string[] surface.

## D-AJ-3 -- no System.Array.Concat / Array.Join statics exist in the BCL

- The .NET array-concatenation surface is System.String.Concat(string[]) and
  System.String.Join(string, string[]); there is no Array.Concat<T> static
  to wire (a grep of the mscorlib surface confirms). The "Concat/Join
  combinations" the case drives are exactly the array-input string
  concatenation family.
