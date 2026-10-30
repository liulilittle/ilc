# B5 fix -- residual gaps (registered, NOT silent)

Scope of this batch: D-1 (non-i4 `Any(pred)`/`Count(pred)`/`All`/`Where`/
`Select`/`First`/`Last` for every element family), D-2 (`List<T>` carrier
dispatch), the R2 root-table abort, the R2 OrderBy/Last(pred) wiring, and the
R3/R4/R5 items.

Everything registered here is one of:

- (a) a precision-only issue with no functional effect,
- (b) a documented semantic deviation,
- (c) an operator/shape whose kernel does not exist, which raises a
  COMPILE-TIME `NotSupportedException`, or
- (d) a shape that is rejected at RUN time with `NotSupportedException` because
  the decision needs a runtime type (`object`/interface keys).

Claims of the form "never a silent wrong value" are deliberately NOT made here:
the audit rounds found several silent wrong values (the zip family mix-up, the
object-key string ordering, the elemKind-less materializers), so this document
enumerates what is checked instead of asserting a global property.

## 1. Type-tracking precision (D-6) -- registered, no functional impact

`LinqRefArrayTypeName` / `LinqRefElementTypeName` (MethodMap.Linq.cs) return
`System.Object[]` / `Object` for a non-string reference element type (they only
special-case `0x0E` = String). Verified with the audit probes `classarr`,
`fieldprobe`, `chainref`: all MATCH. The tracked name only feeds downstream
member resolution hints; the emitted call and the runtime carrier are correct.
Fixing this needs a real type-name table for arbitrary reference generic args
(the byte alone cannot name a class), so it is deferred rather than guessed.

The tracked List receiver NAME is likewise a heuristic and is known to be
unreliable (MEASURED in R5: a `List<double>` receiver inside a method that also
builds a `List<int>` was tracked as `System.Collections.Generic.List`1_int`, and
`il2cpp_linq_list_zip` then read the double cells as int32). R5 therefore made
the MethodSpec generic argument (`genArgs[0]`) the ELEMENT AUTHORITY in
`LinqListKind`; the name suffix is used only when the call carries no MethodSpec
at all (the `List<T>` instance-method forms). Any future operator that reads the
element family must follow that order: metadata first, tracked name second.

## 2. String ordering keys -- two-layer LOUD (was a silent defect)

`Enumerable.OrderBy` / `OrderByDescending` with a `Func<TSource,string>` key
selector throws `NotSupportedException` at TRANSPILE time.

Why: net48's `Comparer<string>.Default` is CULTURE-aware (it sorts `"a"` before
`"A"` and applies OS-NLS collation weights). This runtime has no collation
infrastructure -- `stringext2.cpp` documents that the culture comparison modes
degrade to an ASCII case fold and that "net48's OS-NLS collation is not
replicated"; `string.CompareTo` is ordinal. An ordinal comparison would
therefore silently misorder mixed-case input.

A STATIC `string` key is caught by the transpiler, but an `object`/interface key
must be judged at RUN time: `object[] { "b", "A", "c", "a" }.OrderBy(x => x)`
has `TKey == System.Object`, and its keys happen to BE strings. `CompareRefKeys`
(linq_sort.cpp) therefore throws `NotSupportedException` when either key
instance is a `System.String` (registered lock:
`tests/cases/loud-p0-16-objkey-loud`). Every other reference key type still goes
through `IComparable.CompareTo` (Comparer<T>.Default semantics), null-first.

**NULL keys are NOT rejected.** The null checks run BEFORE the string check,
because net48 orders a null key before every non-null key without consulting the
comparer at all -- so `object[] { null, "a" }.OrderBy(x => x)` is answerable and
returns `{ null, "a" }` (and the descending form puts null last). Rejecting that
would be a LOUD misfire. Registered lock: `tests/cases/b5nullkey` (ascending,
descending, two nulls, and null mixed with a non-string `IComparable` key). A
comparison of two NON-null keys where at least one is a string still throws.

Honest history: the ordinal branch was introduced by this batch (commit
2a2cf2cf's `SortRefsKeyCopy`/`CompareRefKeys`), and the first e2e case was
narrowed to single-case input to avoid the mismatch. Both were wrong; the
current state is the compile-time rejection (arrays and `List<T>`, lock:
`tests/cases/loud-p0-14-strkey-loud`) plus the runtime instance check above.

## 3. Documented semantic deviations

- Every `List<T>` / array operator materializes EAGERLY: `var q = l.Where(p);
  l.Add(x); foreach (q)` sees the snapshot, not the late mutation. This is the
  stage-1 design deviation documented in `linq_map.h`.
- `string.Join(Array)` over a NARROW value array (`sbyte[]`/`byte[]`/`short[]`/
  `ushort[]`/`char[]`/`bool[]`) formats through `stringext2.cpp`
  `ValueArrayElement`, whose table has no signed/unsigned-narrow or bool entry
  (elemSize 1 cannot distinguish sbyte from byte; elemSize 2 cannot distinguish
  short from ushort from char). This is a PRE-EXISTING shared-formatter
  limitation, independent of LINQ: LINQ itself returns the correct cells (the
  e2e asserts them with explicit casts). A fix belongs to the string/format
  domain, not this batch.
- Narrow-family result arrays therefore carry `elemKind 0` (their family travels
  as the element-kind LITERAL the transpiler appends, never as a cell kind):
  `ValueElemKindFor` maps only 1 int32 / 2 int64 / 3 single / 4 double /
  5 uint32. `string.Join` over such an array hits the limitation above.
- CORRECTION (R6) to an earlier report: the claim that "the `zip_i4` shortcut
  now requires `resKind == 0` and exact int32 cells on both sides" was WRONG on
  both counts -- `il2cpp_linq_zip_i4` has **no `resKind` parameter at all**.
  What actually happened: the shortcut was **removed from the List path
  entirely**. `il2cpp_linq_list_zip` now delegates to
  `il2cpp_linq_zip_family` unconditionally, and the transpiler emits
  `il2cpp_linq_zip_family` for every Zip (see section 5b: `il2cpp_linq_zip_i4`
  has no caller). The truncation defect is fixed by routing through the
  result-family-aware entry, not by adding a guard to `zip_i4`.
- `Zip` with a `UInt64` (`ulong`) TResult is mapped onto the int64 result family
  (`resKind` 2): the cell width and the write ABI are identical, but a value
  above `Int64.MaxValue` prints through the int64 formatter, so an `ulong`
  projection is NOT bit-exact in `string.Join`. Registered, not silent (the
  cells hold the correct 8 bytes).
- Float results ride the 8-byte double carrier in a cell typed `elemKind 3`
  (the low 4 bytes are the float's own bits) -- the same convention as
  `il2cpp_linq_select_wide`. Readers take elemKind first, so a carrier array fed
  back as a source reads correctly.

## 4. Kernels that do not exist yet -> still LOUD (never silent)

| Operator | Shape | Reason |
| --- | --- | --- |
| `Where` / `Select` | `Func<T,int,...>` (index overload) over a **LIST** source | no list index kernel (the ARRAY forms are wired: `where_idx_i4`, `where_idx_ref`, `select_idx_i4_*`, `select_idx_ref_ref`) |
| `OrderBy` / `OrderByDescending` | `Func<T,int,TKey>` (index key selector) | no index key-selector entry |
| `OrderBy` / `OrderByDescending` | a user-STRUCT key (a 0x11 value type that is neither `System.DateTime` nor `System.Guid`) | no field-wise comparison carrier (impl-linq3 B wired the DateTime/Guid carriers over the i4/i8/dec/ref sources; impl-linqleft added the decimal GroupBy bucket) |
| `OrderBy` / `OrderByDescending` | a `System.String` key -- STATIC (`TKey == string`) | transpile-time rejection (no collation) |
| `OrderBy` / `OrderByDescending` | a key whose RUNTIME type is `System.String` (`object`/interface key) | runtime rejection in `CompareRefKeys` (no collation) |
| `ThenBy` / `ThenByDescending` | LIST receiver, or a reference-element source | the chained-sorter fold only rewrites the ARRAY forms (impl-linqleft added the decimal-element and decimal-bucket forms with `System.DateTime` / `System.Guid` secondary keys) |
| `GroupBy` | non-int32 key family / non-int32 list | the stage-1 grouping surface is int32 keys + int32 elements |
| `Sum`/`Min`/`Max`/`Average` | reference-element source or LIST | numeric aggregates over a reference family have no kernel |
| `Aggregate` | seed/accumulator family outside i4/i8/f/d/ref/dec/dt/ts/guid | no kernel (impl-aggvt wired the DateTime/TimeSpan/Guid CELL families over a decimal accumulator: `il2cpp_linq_aggregate_seed_{dt,ts,guid}_dec` + the 6 `_rs` projections each, element read at the source array cell width 8/8/16 B) |
| `Aggregate` | a USER-STRUCT source with any accumulator | no by-value callback ABI for the struct element (impl-aggvt keeps the transpile-time LOUD; the DateTime/TimeSpan/Guid cell families are the only value-struct folds) |
| `ToList` | reference-element array | `Resolve_ToList_String_ThrowsLoud` (pre-existing) |
| `Last(pred)` / `LastOrDefault(pred)` | `List<string>` (reference-element list) | no list predicate-last entry; the array entry exists |
| `Zip` | TResult = `sbyte`/`byte`/`short`/`ushort` | transpile-time rejection: the wide/narrow zip surface writes 4- or 8-byte result cells, so a 1/2-byte result cell has no ABI |
| `Zip` | TResult = `bool` / `char` | same as above (1/2-byte cell), and the shared formatter has no bool/signed-narrow entry |
| `Zip` | TResult = a reference type, value-family receiver | the value-cell zip surface cannot write a reference slot |
| `Zip` | ANY reference-element source (array or `List<T>`), any TResult | **transpile-time rejection.** This used to CRASH, not throw: the reference Zip branch called the selector through `Il2CppObject*(Il2CppObject*, Il2CppObject*)`, so a value TResult (int32 in EAX) was stored as a GC pointer through the write barrier -- measured `exit=-1073741819` (0xC0000005) for `string[]{"a","bb"}.Zip(a, (x,y) => x.Length + y.Length)`. Both array arms and the `List<T>` arm now reject the shape at transpile time, and the runtime reference branch was REMOVED (section 5c) so it cannot be re-entered. |

## 5. Export inventory -- BOTH scopes (this replaces the earlier "0 unwired")

The earlier claim "0 truly-unwired exports" only checked whether the
transpiler REFERENCED a symbol, never whether the symbol had a DEFINITION. That
masked 17 declared-but-undefined symbols (the R3 link failures). The inventory
must be read on both axes:

### 5a. Referenced by the transpiler but with NO definition

NONE at the time of writing. Verified with `nm --defined-only -C
libil2cpp_runtime.a` against every `il2cpp_linq_*` declaration in
`linq.h` / `linq_map.h` / `linq_list.h` / `linq_sort.h`:
`declared=308, missing=0` (the R3 batch implemented `where_{i8,d,f,narrow}`,
`select_wide`, `first_{i8,d,f,narrow}`, `first_or_default_*`,
`last_{i8,d,f,narrow}`, `last_or_default_*`; the R5 batch re-cut
`zip_wide`/`list_zip` and added the `*_pred_i4` last variants).

### 5b. Declared and defined but NOT referenced by the transpiler

These are runtime-internal helpers or superseded entries -- NOT gaps:

- `il2cpp_linq_list_{shortName}_{suffix}` / `..._sel_{psuffix}` -- Sum/Min/Max/
  Average over a list (built by `$"..."` interpolation).
- `il2cpp_linq_aggregate_seed_{acc}_rs_{res}` / `..._rs_{res}` -- built by
  `cppBase + resSuffix` in the Aggregate arm.
- `il2cpp_linq_grouping_*` / `il2cpp_linq_grouping_result_*` -- runtime-side
  class/enumerator symbols consumed by `linq_map.cpp` + `arraysort.cpp`.
- `il2cpp_linq_list_snapshot` -- internal helper of `linq_list.cpp`.
- `il2cpp_linq_zip_i4` (linq_map.cpp) -- **defined but now has NO CALLER**. The
  four transpiler Zip emission sites all target `il2cpp_linq_zip_family`
  (verified: `grep il2cpp_linq_zip_family src/il2cppc/Emit/MethodMap.Linq.cs`
  -> 4 sites; `grep il2cpp_linq_zip_i4 src/il2cppc` -> only a comment), and the
  former internal caller in `il2cpp_linq_list_zip` now delegates to the unified
  entry. It is kept only as the int32-only fast-path ABI; if a future batch does
  not add a caller, deleting it (declaration + definition) is the correct
  cleanup.

### 5c. Unreferenced legacy declarations (honest statement)

`il2cpp_linq_order_by_ref` / `il2cpp_linq_order_by_string` take a 2-arg
`Comparison<T>`, while `Enumerable.OrderBy` passes a 1-arg key selector: a
1-arg delegate invoked through a 2-arg signature is undefined behaviour, so
neither entry can serve `Enumerable.OrderBy`. **They have NO CALLER**: `grep`
over `src/il2cppc` finds only the explanatory comment in `MethodMap.Linq.cs`
(no emission site exists), and `linq_sort.h` documents them as the low-level
`Comparison<T>` ABI kept for a future caller. They are NOT "wired", they are
unreferenced declarations -- if a future batch does not add a caller, deleting
them (declaration + definition) is the correct cleanup.

**Removed in R6: the reference branch of `il2cpp_linq_zip_family`** (it was
`linq_list.cpp` ~1277-1302). It was unreachable-but-dangerous dead code: with
the transpiler now rejecting every reference-element Zip, the only way in was a
hand-written C++ caller, and that path stored a value-result thunk's EAX as a GC
pointer. `grep ZipRefRefFn|ZipRefRefThisFn` showed exactly two users (the two
typedefs plus that branch), so the branch AND both typedefs were deleted; the
same conditions now raise `il2cpp_throw_not_supported_exception` instead.

## 6. Rooting table

All four `RootScope` implementations (`linq_list.cpp`, `linq.cpp`,
`linq_map.cpp`, `linq_sort.cpp`) grow a `std::vector<void**>` on demand; there
is no fixed capacity and no `std::abort()`. An allocation failure is reported as
`System.OutOfMemoryException` via `il2cpp_throw_new` (there is no dedicated
`il2cpp_throw_out_of_memory_exception` factory in this repository), so a real
OOM is no longer disguised as `NotSupportedException`.

## 7. The zip SIDE descriptor (R5) -- why it is a separate enumeration

`il2cpp_linq_zip_wide` reads each source by its OWN family (the two sources of a
`Zip` may differ, e.g. `List<double>.Zip(int[])`) and calls the selector through
the ABI of its STATICALLY DECLARED parameter types (int32_t for the int32 slot
and every narrow family, int64_t for i8, double for float/double). The side
descriptor is `IL2CPP_LINQ_NARROW_*` (0..6) plus `IL2CPP_LINQ_ZIP_I4/I8/F/D`
(7..10) and is deliberately NOT the `IL2CPP_LIST_KIND_*` space: the two share
the small integers with DIFFERENT meanings (LIST_KIND SBYTE=5 vs NARROW CHAR=5,
LIST_KIND BYTE=6 vs NARROW UINT=6), and passing one through as the other reads a
1-byte cell as char16_t/uint32 -- a measured out-of-bounds read. `linq_list.cpp`
translates through the explicit named table `ZipSideOfListKind`.

The second source's family travels as an appended literal (`secondKind`) derived
from the SAME MethodSpec authority as `kind`.

## 8. Batch 5 -- the String/StringBuilder + Convert/Crypto residual LOUD (WIRED)

The batch-5 scan anchors (MethodMap.String.cs:605, MethodMap.StringBuilder.cs
:149/311/320/357/376, MethodMap.Crypto.cs:65/123/260, MethodMap.Convert.cs:282,
MethodMap.ConvertExtra.cs:93/124/153/189) were re-checked against a live
transpile. The genuinely-static-but-unwired members are now wired (each with an
e2e difftest + unit test); the truly-unwireable ones stay LOUD with a reason.

### 8a. String.Join(start/count)

`String.Join(string, string[], int, int)` and `String.Join(string, object[],
int, int)` were ALREADY wired (`icall_string_join_range_s` /
`_range_c`). The `MethodMap.String.cs:605` throw is the catch-all for arities
that net48 does not define; no residual form was found (a probe of the
params-string / params-object `Join(sep, a, b, ...)` shapes transpiles clean --
the compiler binds them to the `string[]` overload).

### 8b. StringBuilder.AppendFormat(IFormatProvider, ...)

The params-object[] provider form was wired; the explicit 1/2/3-argument
provider forms were LOUD. Now wired to the new
`icall_stringbuilder_append_format_{1,3}_provider` (the 2-argument form already
existed). e2e: `sb-provider-fmt`.

**BATCH 7 follow-up:** the RNG named-CSP ctors (`RNGCryptoServiceProvider(string)`
/ `(byte[])` / `(CspParameters)`) are now wired too -- see 8f. The
`CspParameters` ctor maps the CLASS (0x12) argument onto the same no-op
`icall_rng_ctor` (net48 delegates to `this((CspParameters) null)`; the OS CSPRNG
is used regardless of the container name). The mapping also fixes a latent
transpiler fold desync: `ParamCount` now carries the explicit IL arity (the
dropped argument is popped from the fold's shadow stack), so a method that also
has a loop no longer fails the fold with "fall-through stack height 0 != join
slots 2". e2e: `rng-csp-ctor`.

### 8c. DefaultInterpolatedStringHandler.AppendFormatted<T> over a VALUE type

The primitive slots (int/long/double/bool/char/string) were wired; a generic
value type `T` (0x11 -- decimal / DateTime / Guid) was LOUD for the 1/2/3-arg
forms. Now wired: the MethodSpec generic-arg NAME table names the closed type,
the transpiler boxes the value slot (`box:dec` / `box:dt` / `box:guid` /
`box:ts`), and the runtime renders the box through the composite engine
(`icall_stringbuilder_append_o[_w/_f/_wf]`). The composite engine also learned
the DateTime/Guid FORMAT specifier (net48 applies `IFormattable.ToString(format,
provider)` in `string.Format` too -- probe-pinned: `string.Format("{0:yyyy}",
dt)` -> "2020"). e2e: `interp-valtype` (net8 oracle -- the handler is a net6+
lowering).

**BATCH 7 follow-up:** a USER struct/enum now boxes through the new
`box:us:<name>` cast. `CodeGenerator.EmitUserStructBoxCast` resolves the
TypeDef and applies the GC-safety gate: an enum boxes through `icall_box_enum`
(the width-aware int64 payload), a reference-free user struct through
`icall_box_user_struct`, and a user struct with GC reference fields is REJECTED
(icall_box_user_struct would place an object reference at slot 1 and the boxed
mark scan indexes refFieldBitmap from the object header -- a ref field would be
miscounted as a pure header, a GC-safety leak). The composite engine honors the
format specifier for a boxed enum (G/D/F/X -- net48 IFormattable.ToString) and
falls back to the reflected ToString / ValueType name for a user struct. e2e:
`interp-userstruct`.

### 8d. HMAC Key property + keyless ctor

`HMAC Key` get/set and `HMACSHAx()` were LOUD. Now wired over the net48
`HMAC.Key` / `InitializeKey` contract: the getter returns a CLONE of the stored
key; the setter runs InitializeKey (null -> NullReferenceException, a key longer
than the block size is hashed first, otherwise cloned); the keyless ctor
generates a random key of the algorithm's block size (64 for SHA-1/256, 128 for
SHA-384/512). `icall_hmac_ctor` now normalizes the key the same way (so the
getter reports the normalized key). e2e: `hmac-key`.

### 8e. Convert.ToChar(string) + Convert.ToXxx(string)

The throwing-parse string conversions were LOUD. Now wired to
`icall_convert_to_char_string` and
`icall_convert_to_{byte,sbyte,int16,uint16,uint32,uint64,single}_string` with
the net48 contract: null -> 0 (the Convert "null means zero" rule -- EXCEPT
`ToChar(string)`, where null -> ArgumentNullException, probe-pinned), malformed
text -> FormatException, an out-of-range magnitude or a negative input to an
unsigned target -> OverflowException, and `ToSingle` throws OverflowException
beyond Single.MaxValue (not a silent Infinity). e2e: `conv-str-parse`.

**BATCH 7 follow-up:** the value-source residuals are now wired (each with the
net48 Reference-Source contract):
* `ToSingle(bool)` -> `icall_conv_r4_i4` (`value ? 1f : 0f`).
* `ToSingle(char)` -> `icall_convert_to_single_char`, a new throwing icall
  (net48 char has no `IConvertible.ToSingle`; the implicit char->float widening
  exists but Convert does not expose it -- probe-pinned against the oracle).
* `ToSingle(uint)` -> `icall_conv_r4_u4` over the new `"u4"` reinterpret cast.
* `ToUInt32(uint)` -> `icall_conv_u4_i4` (the identity bit pattern).
* `ToDouble(uint)` -> `icall_conv_d_u4` over `"u4"` (exact).
* `ToDouble(float)` -> `icall_conv_d_d`, a new identity helper (the float
  source rides an already-widened double slot).

The `"u4"` cast code reinterprets the int32 carrier (zero-extension, so
uint >= 2^31 stays positive). e2e: `conv-b7`.

### 8f. Still LOUD (unchanged, each with a reason)

* `HashAlgorithm.Create(string)` -- depends on machine-level crypto config
  (CryptoConfig); not statically analyzable from the call site.
* `HashAlgorithm.Hash` -- net48 caches the last ComputeHash digest and returns
  a clone (NullReferenceException before the first call, ObjectDisposedException
  after Clear); this runtime's ComputeHash is stateless per call, so the cached
  value would need a new per-instance ref field (a class-descriptor change).
  WIRED by batch 6 (the Il2CppHashState hashValue field).
* `HashAlgorithm.Clear()` -- net48 disposes the object (a later `Hash`
  throws ObjectDisposedException); this runtime's Dispose is a no-op and tracks
  no disposed state. WIRED by batch 6 (the disposed flag).
* `Convert.ToByte/.../ToSingle` over a `decimal` source (the decimal-source
  narrow targets are wired by batch 6; the `char`-source narrow targets by
  batch 6 too), and the `object`-source Convert overloads.
* `DefaultInterpolatedStringHandler.AppendFormatted<T>` over a value type OTHER
  than decimal/DateTime/Guid/TimeSpan and a user struct/enum (batch 7) -- a
  BCL value type with no generated descriptor (e.g. a `KeyValuePair`) still has
  no box helper; the cast rejects it LOUD.

### 8g. HashAlgorithm object surface (HashSize / Initialize) -- WIRED

`HashSize` (the digest size in BITS: MD5 128 / SHA-1 160 / SHA-256 256 /
SHA-384 384 / SHA-512 512) and `Initialize` (a no-op -- ComputeHash is stateless
per call) were LOUD. Now wired to `icall_<algo>_get_hash_size` /
`icall_<algo>_initialize`. e2e: `hash-obj-surface`.

### 8h. Aes.TransformBlock (v2 streaming) -- WIRED (batch 7)

`ICryptoTransform.TransformBlock` on the AES transform was LOUD (v2). Now wired
to `icall_aes_transform_block` with the net48 `RijndaelManagedTransform`
semantics: inputCount is a positive multiple of the block size; encryption
chains every block with the running IV; decryption buffers the last ciphertext
block (the padding can only be stripped by `TransformFinalBlock`). The
`Il2CppAesTransformObject` gains the running chain + the decrypt pending block
(non-reference bytes, so the refFieldBitmap and key/iv/ctx offsets are
unchanged); `TransformFinalBlock` uses the running chain and resets it (net48
`Reset`). e2e: `aes-transform-block`.

## 8i. Batch 8 -- the Convert object/value-source residuals + the full residual-LOUD re-scan

Batch 8 consumes the three residual items left by batches 1-7 (see 8f) and
re-scans every `NotSupportedException`/LOUD site under `src/il2cppc/Emit/MethodMap*.cs`.
The scan found **437** throw sites; the independent review classifies **303
analyzable (must implement)** / **134 not-analyzable (whitelist)**. Batch 8
implements the analyzable items in the Convert/Base64 domain (the highest-value
residuals named by the review's Top-20); the full per-site table is 8i.4.

### 8i.1. The three residual items (judged)

| item | judgment | action |
|---|---|---|
| `HashAlgorithm.Create(string)` | **not statically analyzable** (machine-level CryptoConfig) | whitelisted (8i.2); no loud-contract case existed, so none was added |
| `Convert.ToXxx(object)` | **statically analyzable** (the boxed value's runtime type is known) | **WIRED** -- `icall_convert_to_<target>_obj` boxed dispatch + the `0x1C` arms |
| `box:us:<BCL value type>` (no generated descriptor) | **not statically analyzable** at the call site | the `EmitUserStructBoxCast` gate is correct: a BCL value type with no generated TypeDef has no box helper (the GC mark scan needs the descriptor); see 8i.3 |

### 8i.1b. Convert / ConvertExtra / Base64 analyzable items -- WIRED

Every analyzable item in the three Convert-domain files is now wired (each with
the net48 oracle pinned by the `conv-obj` difftest and a unit test):

| member(s) | wiring |
|---|---|
| `Convert.ToXxx(object)` (0x1C) | `icall_convert_to_{int32,int64,uint32,uint64,byte,sbyte,int16,uint16,double,single,bool,char,decimal}_obj` boxed dispatch |
| `Convert.ToDouble(bool)` | `icall_conv_d_i4` (`value ? 1.0 : 0.0`) -- the batch-5 sweep wrongly claimed ICE |
| `Convert.ToDouble(char)` | `icall_convert_to_double_char` (net48 ICE) |
| `Convert.ToBoolean(long/ulong)` | `icall_convert_to_bool_i8` (`value != 0`, full 64-bit) |
| `Convert.ToBoolean(float/double)` | `icall_convert_to_bool_d` (`value != 0`) -- wrongly claimed ICE |
| `Convert.ToBoolean(char)` | `icall_convert_to_bool_char` (net48 ICE) |
| `Convert.ToBoolean(decimal)` | `icall_convert_to_bool_dec` (`value != 0`) -- wrongly claimed ICE |
| `Convert.ToChar(long/ulong)` | `icall_convert_to_char_i8`/`_u8` (CHECKED UTF-16 range) |
| `Convert.ToChar(bool/float/double)` | `icall_convert_to_char_bool`/`_d` (net48 ICE) |
| `Convert.ToString(uint/ulong/float/decimal)` | `icall_uint32_to_string`/`icall_uint64_to_string`/`icall_convert_to_string_d`/`icall_decimal_to_string` (1-arg + provider forms) |
| `Convert.ToDecimal(value, IFormatProvider)` | the 2-arg provider form drops the provider and routes to the 1-arg arm |
| `Convert.ToBase64String(byte[], int, int[, options])` | `il2cpp_convert_to_base64_string_range`/`_options`/`_range_options` (InsertLineBreaks CRLF every 76 chars) |
| `Convert.FromBase64CharArray(char[], int, int)` | `il2cpp_convert_from_base64_char_array` |

The `conv-obj` difftest (sha `6fd4b47a...`, 2928B) pins the whole surface; the
`MethodMapTests.ConvertObj` unit test (28/28) pins the mappings.

### 8i.2. Contract whitelist entry -- `HashAlgorithm.Create(string)`

* **Member full name:** `System.Security.Cryptography.HashAlgorithm.Create(string)`
  (also `System.Security.Cryptography.SymmetricAlgorithm.Create(string)`,
  `System.Security.Cryptography.AsymmetricAlgorithm.Create(string)`,
  `System.Security.Cryptography.KeyedHashAlgorithm.Create(string)` -- the same
  CryptoConfig by-name factory family).
* **Rejection reason:** the factory resolves the algorithm NAME through
  `System.Security.Cryptography.CryptoConfig.CreateFromName`, whose mapping is
  loaded from the machine-level crypto configuration (machine.config
  `<cryptoClasses>`/`<cryptoClassAlias>`) plus a set of hardcoded aliases. The
  transpiler has no machine configuration and cannot reproduce the per-machine
  mapping from the call site.
* **net48 behavior:** `HashAlgorithm.Create(string algName)` returns
  `(HashAlgorithm)CryptoConfig.CreateFromName(algName)`; an unknown name throws
  `CRYPT_E_|CryptographicException` (throwOnError), a name mapped to a non-hash
  type throws `InvalidCastException`. The parameterless `HashAlgorithm.Create()`
  returns `new SHA1CryptoServiceProvider()` (obsolete).
* **Judgment basis:** .NET Framework 4.8 Reference Source --
  `System.Security.Cryptography.HashAlgorithm.Create(String algName)` is
  `(HashAlgorithm)CryptoConfig.CreateFromName(algName)`, and
  `CryptoConfig.CreateFromName` consults the process-wide `s_config` table
  populated from machine.config. The mapping is therefore machine-state, not a
  static function of the argument. (The concrete-algorithm `Create()` parameterless
  factories -- `SHA256.Create()` etc. -- ARE wired to `icall_<algo>_create`.)
* **Registration:** no loud-contract case existed for this member, so none was
  added to `scripts/sweep-exclude.txt`; the rejection is a transpile-time
  `NotSupportedException` with the CryptoConfig reason inline
  (`MethodMap.Crypto.cs:65`).

### 8i.3. `box:us:<name>` for a BCL value type with no generated descriptor

`CodeGenerator.EmitUserStructBoxCast` (CodeGenerator.cs:10792) resolves the
TypeDef from the loaded type list and REJECTS a name with no generated
descriptor. This is correct: `icall_box_user_struct` copies the payload into a
box whose GC mark scan indexes `refFieldBitmap` from the object header, and the
box class descriptor (`&{T}_class`) is only emitted for a generated TypeDef. A
BCL value type that the transpiler never materialized (e.g. a closed
`KeyValuePair``) has neither a `{T}_class` nor a `sizeof({T})` C++ type, so
there is no safe box helper. The reference-free user-struct/enum path IS wired
(batch 7); the GC-reference-field path is rejected for GC safety (a ref field
at slot 1 would be miscounted as a pure header). No change.

### 8i.4. Full residual-LOUD re-scan (`src/il2cppc/Emit/MethodMap*.cs`)

Independent-review classification (437 `NotSupportedException` sites): **303 analyzable (must implement)** / **134 not-analyzable (whitelist)**.

Judgment key:
* **analyzable** -- the transpiler knows every required input at compile time (generic args / arity / param family / element family / selector shape); only the runtime kernel or the mapping arm is missing (an implementation gap, NOT unanalyzable).
* **not-analyzable** -- one of the four whitelist classes: (1) machine state (`CryptoConfig`/machine.config, culture-aware collation); (2) dynamic dispatch (`dynamic`/untracked `object` args); (3) an unmaterializable runtime descriptor (a BCL value type with no `{T}_class`); (4) a net48 overload/arity that does not exist.

Per-file (A=analyzable, N=not-analyzable):

| file | A | N |
|---|---:|---:|
| MethodMap.Array.cs | 15 | 8 |
| MethodMap.ArrayList.cs | 1 | 0 |
| MethodMap.ArraySort.cs | 4 | 1 |
| MethodMap.Attribute.cs | 0 | 2 |
| MethodMap.Base64.cs | 2 | 1 |
| MethodMap.BigInteger.cs | 0 | 2 |
| MethodMap.BinaryIO.cs | 2 | 11 |
| MethodMap.BitConverter.cs | 0 | 8 |
| MethodMap.Buffer.cs | 1 | 4 |
| MethodMap.Calendar.cs | 1 | 0 |
| MethodMap.Console.cs | 2 | 5 |
| MethodMap.Convert.cs | 11 | 7 |
| MethodMap.ConvertExtra.cs | 1 | 9 |
| MethodMap.Crypto.cs | 0 | 18 |
| MethodMap.CultureInfo.cs | 1 | 1 |
| MethodMap.CustomAttributeExtensions.cs | 0 | 2 |
| MethodMap.DateTime.cs | 3 | 1 |
| MethodMap.DateTimeExtra.cs | 1 | 0 |
| MethodMap.Debug.cs | 4 | 2 |
| MethodMap.Debugger.cs | 0 | 1 |
| MethodMap.Decimal.cs | 0 | 2 |
| MethodMap.Delegate.cs | 1 | 1 |
| MethodMap.DependentHandle.cs | 0 | 1 |
| MethodMap.Dictionary.cs | 0 | 1 |
| MethodMap.Dtoffset.cs | 0 | 2 |
| MethodMap.Encoding.cs | 6 | 0 |
| MethodMap.Enum.cs | 3 | 2 |
| MethodMap.EnvironmentExtra.cs | 0 | 1 |
| MethodMap.Exception.cs | 0 | 1 |
| MethodMap.Format.cs | 0 | 1 |
| MethodMap.GC.cs | 1 | 0 |
| MethodMap.GCHandle.cs | 4 | 0 |
| MethodMap.Guid.cs | 2 | 2 |
| MethodMap.Hash.cs | 2 | 0 |
| MethodMap.HashSet.cs | 0 | 1 |
| MethodMap.IO.cs | 7 | 0 |
| MethodMap.IntPtr.cs | 0 | 1 |
| MethodMap.Legacy.cs | 0 | 6 |
| MethodMap.Linq.cs | 42 | 3 |
| MethodMap.List.cs | 2 | 0 |
| MethodMap.Marshal.cs | 5 | 3 |
| MethodMap.Math.cs | 1 | 0 |
| MethodMap.MathExtra.cs | 3 | 0 |
| MethodMap.Module.cs | 1 | 0 |
| MethodMap.Net.cs | 2 | 0 |
| MethodMap.Object.cs | 2 | 0 |
| MethodMap.Process.cs | 2 | 1 |
| MethodMap.ProcessInfo.cs | 1 | 0 |
| MethodMap.Queue.cs | 1 | 0 |
| MethodMap.RandomExtra.cs | 1 | 0 |
| MethodMap.Reflect.cs | 15 | 2 |
| MethodMap.ReflectHandle.cs | 3 | 0 |
| MethodMap.ReflectionExtensions.cs | 3 | 0 |
| MethodMap.Regex.cs | 0 | 3 |
| MethodMap.RuntimeHelpers.cs | 0 | 11 |
| MethodMap.SafeHandle.cs | 1 | 0 |
| MethodMap.SortedList.cs | 3 | 0 |
| MethodMap.Stack.cs | 1 | 0 |
| MethodMap.StreamIO.cs | 14 | 0 |
| MethodMap.String.cs | 21 | 1 |
| MethodMap.StringBuilder.cs | 27 | 0 |
| MethodMap.StringIO.cs | 7 | 0 |
| MethodMap.Task.cs | 1 | 0 |
| MethodMap.Thread.cs | 1 | 0 |
| MethodMap.TimeExt2.cs | 3 | 0 |
| MethodMap.TimeSpan.cs | 1 | 0 |
| MethodMap.TimeZone.cs | 7 | 1 |
| MethodMap.Type.cs | 43 | 2 |
| MethodMap.UIntPtr.cs | 0 | 1 |
| MethodMap.Uri.cs | 2 | 0 |
| MethodMap.ValueType.cs | 2 | 1 |
| MethodMap.Version.cs | 2 | 0 |
| MethodMap.WeakReference.cs | 2 | 0 |
| MethodMap.Xml.cs | 6 | 0 |

Per-site table (batch-8 implemented sites marked `WIRED`):

| file:line | member | condition | judgment |
|---|---|---|---|
| MethodMap.Array.cs:86 | Sort | 娉涘瀷鍙傛暟鏁?arity 涓嶅睘 net48 閲嶈浇 | not-analyzable |
| MethodMap.Array.cs:196 | Sort | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:222 | Sort | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:269 | BinarySearch | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:290 | IndexOf | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:309 | LastIndexOf | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:345 | Empty | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:348 | Empty | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:381 | Reverse | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:423 | Copy | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:433 | ConstrainedCopy | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Array.cs:471 | FindIndex | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:500 | Find | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:524 | FindLast | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:545 | FindLastIndex | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:566 | ForEach | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:590 | FindAll | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:610 | Exists | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:629 | TrueForAll | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:643 | ConvertAll | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:672 | Resize | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Array.cs:730 | GetValue | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Array.cs:742 | SetValue | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ArrayList.cs:51 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ArraySort.cs:62 | Sort | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.ArraySort.cs:77 | Sort | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.ArraySort.cs:146 | IndexOf | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ArraySort.cs:173 | LastIndexOf | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ArraySort.cs:222 | BinarySearch | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Attribute.cs:174 | IsDefined | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Attribute.cs:180 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Base64.cs:32 | ToBase64String | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable (WIRED batch 8) |
| MethodMap.Base64.cs:40 | FromBase64String | 杩愯鏃剁己 helper锛堝疄鐜扮己鍙ｏ級 | not-analyzable |
| MethodMap.Base64.cs:44 | FromBase64CharArray | 杩愯鏃剁己 helper锛堝疄鐜扮己鍙ｏ級 | analyzable (WIRED batch 8) |
| MethodMap.BigInteger.cs:173 | CompareTo | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.BigInteger.cs:179 | Equals | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.BinaryIO.cs:78 | Write | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.BinaryIO.cs:88 | Write | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.BinaryIO.cs:94 | Write | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.BinaryIO.cs:101 | Write | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.BinaryIO.cs:109 | Write | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.BinaryIO.cs:115 | Write | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.BinaryIO.cs:122 | Write | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.BinaryIO.cs:142 | Write | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BinaryIO.cs:143 | Write | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BinaryIO.cs:157 | Dispose | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BinaryIO.cs:165 | get_BaseStream | 杩愯鏃剁己 icall锛堝疄鐜扮己鍙ｏ紝缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.BinaryIO.cs:209 | Dispose | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BinaryIO.cs:217 | get_BaseStream | 杩愯鏃剁己 icall锛堝疄鐜扮己鍙ｏ紝缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.BitConverter.cs:85 | GetBytes | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BitConverter.cs:87 | GetBytes | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BitConverter.cs:101 | SingleToInt32Bits | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | not-analyzable |
| MethodMap.BitConverter.cs:105 | Int32BitsToSingle | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BitConverter.cs:129 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BitConverter.cs:134 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BitConverter.cs:156 | (鍔ㄦ€佸潡/鍏滃簳) | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.BitConverter.cs:157 | (鍔ㄦ€佸潡/鍏滃簳) | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Buffer.cs:26 | BlockCopy | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Buffer.cs:32 | ByteLength | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Buffer.cs:42 | MemoryCopy | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Buffer.cs:57 | GetByte | 杩愯鏃剁己 icall锛堝疄鐜扮己鍙ｏ紝缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Buffer.cs:59 | SetByte | 杩愯鏃剁己 icall锛堝疄鐜扮己鍙ｏ紝缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Calendar.cs:96 | ToDateTime | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Console.cs:151 | Write | net48 鏃犳閲嶈浇/arity | not-analyzable |
| MethodMap.Console.cs:289 | WriteLine | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Console.cs:310 | WriteLine | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Console.cs:356 | Dispose | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Console.cs:388 | Dispose | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Console.cs:414 | Dispose | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Console.cs:417 | Dispose | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Convert.cs:78 | ToInt32 | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | not-analyzable |
| MethodMap.Convert.cs:90 | ToInt32 | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Convert.cs:127 | ToInt64 | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | not-analyzable |
| MethodMap.Convert.cs:129 | ToInt64 | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Convert.cs:162 | ToDouble | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:165 | ToDouble | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.Convert.cs:227 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:238 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:242 | ToBoolean | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Convert.cs:258 | ToBoolean | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:260 | ToBoolean | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:261 | ToBoolean | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:266 | ToBoolean | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:270 | ToChar | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Convert.cs:283 | ToChar | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:285 | ToChar | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:286 | ToChar | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable (WIRED batch 8) |
| MethodMap.Convert.cs:292 | ToChar | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable (WIRED batch 8) |
| MethodMap.ConvertExtra.cs:73 | ToUInt16 | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.ConvertExtra.cs:110 | ToUInt16 | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.ConvertExtra.cs:116 | ToUInt32 | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.ConvertExtra.cs:143 | ToUInt32 | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.ConvertExtra.cs:149 | ToUInt64 | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.ConvertExtra.cs:172 | ToUInt64 | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.ConvertExtra.cs:178 | ToSingle | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.ConvertExtra.cs:216 | ToSingle | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.ConvertExtra.cs:243 | ToDecimal | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable (WIRED batch 8) |
| MethodMap.ConvertExtra.cs:266 | ToDecimal | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:65 | Create | 渚濊禆鏈哄櫒绾?CryptoConfig/machine.config 閰嶇疆 | not-analyzable |
| MethodMap.Crypto.cs:69 | Create | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:74 | Create | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:112 | TransformBlock | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:125 | TransformBlock | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:133 | TransformFinalBlock | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:142 | TransformFinalBlock | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:201 | get_IV | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:207 | get_IV | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:222 | get_IV | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:243 | CreateDecryptor | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:265 | GetBytes | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:272 | GetNonZeroBytes | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:353 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:365 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:369 | ComputeHash | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Crypto.cs:392 | ComputeHash | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.Crypto.cs:394 | ComputeHash | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.CultureInfo.cs:60 | GetCultureInfo | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| analyzable |
| MethodMap.CultureInfo.cs:76 | get_DisplayName | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| not-analyzable |
| MethodMap.CustomAttributeExtensions.cs:47 | (鍔ㄦ€佸潡/鍏滃簳) | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.CustomAttributeExtensions.cs:62 | IsDefined | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.DateTime.cs:58 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.DateTime.cs:127 | Subtract | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.DateTime.cs:182 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.DateTime.cs:183 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.DateTimeExtra.cs:68 | ToUniversalTime | 杩愯鏃剁己 icall锛堝疄鐜扮己鍙ｏ紝缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Debug.cs:65 | Assert | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Debug.cs:71 | WriteLine | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | not-analyzable |
| MethodMap.Debug.cs:72 | WriteLine | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Debug.cs:77 | Fail | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Debug.cs:87 | Write | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Debug.cs:91 | WriteLine | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Debugger.cs:55 | Log | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Decimal.cs:200 | CompareTo | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Decimal.cs:207 | Equals | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Delegate.cs:54 | RemoveAll | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Delegate.cs:92 | CreateDelegate | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.DependentHandle.cs:92 | nFree | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Dictionary.cs:102 | (鍔ㄦ€佸潡/鍏滃簳) | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.Dtoffset.cs:114 | CompareTo | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Dtoffset.cs:121 | Equals | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Encoding.cs:139 | GetBytes | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Encoding.cs:155 | GetByteCount | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Encoding.cs:168 | GetCharCount | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Encoding.cs:181 | GetString | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Encoding.cs:201 | GetChars | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Encoding.cs:233 | (鍔ㄦ€佸潡/鍏滃簳) | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Enum.cs:250 | Parse | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | analyzable |
| MethodMap.Enum.cs:276 | TryParse | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| analyzable |
| MethodMap.Enum.cs:279 | TryParse | 娉涘瀷瀹炲弬鏃忔湭鎺ョ嚎锛堝厹搴曡噦锛氬厓绱?閿?缁撴灉鏃忎笉鍦ㄥ凡鎺ョ嚎闆嗗悎锛?| not-analyzable |
| MethodMap.Enum.cs:311 | ToString | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Enum.cs:528 | (鍔ㄦ€佸潡/鍏滃簳) | 鏋氫妇娉ㄥ唽瀹归噺瓒呴檺锛坘MaxRegisteredEnums锛?| not-analyzable |
| MethodMap.EnvironmentExtra.cs:93 | get_StackTrace | 杩愯鏃舵棤鍖归厤 icall | not-analyzable |
| MethodMap.Exception.cs:146 | get_Message | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Format.cs:69 | (鍔ㄦ€佸潡/鍏滃簳) | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| not-analyzable |
| MethodMap.GC.cs:38 | Collect | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.GCHandle.cs:51 | Alloc | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.GCHandle.cs:76 | ToIntPtr | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.GCHandle.cs:83 | FromIntPtr | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.GCHandle.cs:97 | op_Inequality | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Guid.cs:63 | ToString | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Guid.cs:115 | Equals | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Guid.cs:129 | CompareTo | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Guid.cs:133 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Hash.cs:59 | ComputeHash | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Hash.cs:71 | ComputeHash | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.HashSet.cs:400 | (鍔ㄦ€佸潡/鍏滃簳) | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.IntPtr.cs:91 | CompareTo | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.IO.cs:57 | Copy | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.IO.cs:61 | Move | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.IO.cs:98 | Delete | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.IO.cs:119 | GetFiles | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.IO.cs:130 | GetDirectories | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.IO.cs:156 | Combine | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.IO.cs:187 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Legacy.cs:40 | System.Runtime.Remoting.Channels.ChannelServices | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Legacy.cs:184 | nUnload | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Legacy.cs:203 | GetType | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Legacy.cs:212 | System.Runtime.Remoting.ObjectHandle | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Legacy.cs:215 | System.ArgIterator | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Legacy.cs:222 | System.Threading.ReaderWriterLock | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Linq.cs:147 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:202 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:208 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:216 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:282 | f | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:354 | f | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:475 | FirstOrDefault | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:481 | FirstOrDefault | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:517 | FirstOrDefault | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:527 | FirstOrDefault | 鏃犳硶鍒嗙被锛堢己 TypeDef/杞戒綋锛?| analyzable |
| MethodMap.Linq.cs:536 | FirstOrDefault | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Linq.cs:599 | Select | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:616 | Select | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:679 | OrderByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:684 | OrderByDescending | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| not-analyzable |
| MethodMap.Linq.cs:690 | OrderByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:770 | GroupBy | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:827 | GroupBy | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:955 | System.TimeSpan | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Linq.cs:964 | System.TimeSpan | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1080 | Select | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1094 | Select | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1108 | Select | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1224 | OrderBy | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1242 | OrderByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1277 | ThenByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1338 | GroupBy | 鏃犳硶鍒嗙被锛堢己 TypeDef/杞戒綋锛?| analyzable |
| MethodMap.Linq.cs:1395 | GroupBy | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Linq.cs:1470 | Zip | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Linq.cs:1479 | Zip | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1589 | Aggregate | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:1857 | (鍔ㄦ€佸潡/鍏滃簳) | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| not-analyzable |
| MethodMap.Linq.cs:1893 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2340 | Where | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2349 | Select | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2472 | Zip | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2478 | Zip | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Linq.cs:2506 | OrderByDescending | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Linq.cs:2512 | OrderByDescending | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| not-analyzable |
| MethodMap.Linq.cs:2519 | OrderByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2555 | Sum | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2770 | OrderByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2783 | OrderByDescending | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Linq.cs:2852 | Zip | 鍙傛暟/鍏冪礌鏃忔湭鎺ョ嚎 | analyzable |
| MethodMap.Linq.cs:2861 | Zip | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.List.cs:172 | (鍔ㄦ€佸潡/鍏滃簳) | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable |
| MethodMap.List.cs:202 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Marshal.cs:86 | (鍔ㄦ€佸潡/鍏滃簳) | 鍩熸帓闄わ紙閬楃暀/COM/Remoting 鍩燂級 | not-analyzable |
| MethodMap.Marshal.cs:255 | SizeOf | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Marshal.cs:258 | SizeOf | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | not-analyzable |
| MethodMap.Marshal.cs:259 | SizeOf | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Marshal.cs:280 | OffsetOf | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Marshal.cs:282 | OffsetOf | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Marshal.cs:307 | PtrToStructure | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable |
| MethodMap.Marshal.cs:309 | PtrToStructure | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.Math.cs:128 | Round | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.MathExtra.cs:142 | SinCos | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.MathExtra.cs:153 | SplitFractionDouble | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.MathExtra.cs:167 | BigMul | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Module.cs:52 | get_Module | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Net.cs:92 | System.Net.WebClient | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Net.cs:124 | ToString | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Object.cs:340 | System.Reflection.MemberInfo | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Object.cs:363 | System.Reflection.MemberInfo | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Process.cs:98 | Start | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Process.cs:107 | WaitForExit | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Process.cs:196 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.ProcessInfo.cs:46 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Queue.cs:61 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.RandomExtra.cs:26 | Sample | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Reflect.cs:90 | GetValue | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:112 | SetValue | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:125 | GetGetMethod | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:131 | GetSetMethod | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:159 | Invoke | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| not-analyzable |
| MethodMap.Reflect.cs:161 | Invoke | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:179 | AddEventHandler | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:186 | RemoveEventHandler | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:200 | MakeGenericMethod | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:215 | GetParameters | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:337 | CreateInstance | net48 鏃犳閲嶈浇/arity | not-analyzable |
| MethodMap.Reflect.cs:339 | CreateInstance | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| analyzable |
| MethodMap.Reflect.cs:369 | GetType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:396 | get_Name | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:401 | get_ParameterType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:406 | get_Position | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Reflect.cs:412 | IsOut | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ReflectHandle.cs:45 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.ReflectHandle.cs:78 | GetRuntimeType | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.ReflectHandle.cs:83 | GetRuntimeType | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.ReflectionExtensions.cs:36 | GetTypeCode | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ReflectionExtensions.cs:72 | (鍔ㄦ€佸潡/鍏滃簳) | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.ReflectionExtensions.cs:81 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Regex.cs:76 | Replace | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Regex.cs:99 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Regex.cs:104 | (鍔ㄦ€佸潡/鍏滃簳) | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.RuntimeHelpers.cs:50 | GetHashCode | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.RuntimeHelpers.cs:57 | Equals | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.RuntimeHelpers.cs:64 | GetObjectValue | arity/閲嶈浇褰㈢姸鏈帴绾?| not-analyzable |
| MethodMap.RuntimeHelpers.cs:67 | PrepareDelegate | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:69 | PrepareContractedDelegate | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:71 | EnsureSufficientExecutionStack | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:73 | ProbeForSufficientStack | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:75 | ExecuteCodeWithGuaranteedCleanup | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:77 | _RunClassConstructor | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:79 | _RunModuleConstructor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.RuntimeHelpers.cs:86 | InitializeArray | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.SafeHandle.cs:45 | DangerousAddRef | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.SortedList.cs:73 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.SortedList.cs:118 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.SortedList.cs:220 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Stack.cs:56 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:117 | WriteLine | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:126 | Dispose | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:155 | Seek | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:162 | SetLength | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:175 | Read | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:182 | Peek | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:217 | CopyTo | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:224 | ReadByte | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:241 | Write | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:249 | WriteByte | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:287 | System.IO.FileStream | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:319 | System.IO.MemoryStream | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:328 | System.IO.StreamReader | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StreamIO.cs:357 | System.IO.StreamWriter | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:73 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:90 | IndexOf | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:113 | IndexOfAny | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:131 | LastIndexOfAny | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:155 | Replace | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:167 | ToUpperInvariant | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:175 | ToLowerInvariant | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:262 | PadRight | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:273 | Contains | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:281 | GetHashCode | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:297 | Normalize | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:305 | IsNormalized | 渚濊禆鏂囧寲鐩稿叧鎺掑簭锛圕omparer<string> 涓?culture-aware锛?| analyzable |
| MethodMap.String.cs:356 | CompareOrdinal | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:387 | Equals | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:397 | Equals | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:398 | Equals | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:403 | op_Equality | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:418 | op_Inequality | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:456 | Format | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:483 | Concat | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | not-analyzable |
| MethodMap.String.cs:508 | Concat | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.String.cs:605 | Join | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:31 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:46 | Append | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:55 | Append | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:89 | Append | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:111 | Insert | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:121 | Insert | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:132 | AppendLine | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:154 | AppendFormat | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:157 | AppendFormat | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:163 | AppendFormat | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:170 | AppendFormat | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:183 | Replace | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:185 | Replace | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:188 | Replace | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:195 | CopyTo | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:200 | EnsureCapacity | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:205 | Equals | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:319 | AppendFormatted | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | analyzable |
| MethodMap.StringBuilder.cs:331 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:342 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:347 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:367 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:376 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:406 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:413 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:429 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringBuilder.cs:432 | AppendFormatted | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringIO.cs:65 | Read | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringIO.cs:92 | Write | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringIO.cs:99 | WriteLine | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringIO.cs:124 | Dispose | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.StringIO.cs:126 | Dispose | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.StringIO.cs:142 | Write | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.StringIO.cs:148 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Task.cs:114 | (鍔ㄦ€佸潡/鍏滃簳) | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Thread.cs:148 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.TimeExt2.cs:59 | Parse | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeExt2.cs:85 | ParseExact | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeExt2.cs:101 | TryParseExact | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeSpan.cs:34 | .ctor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:123 | GetUtcOffset | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:160 | ToLocalTime | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:165 | ToUniversalTime | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:174 | IsDaylightSavingTime | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:182 | IsInvalidTime | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:195 | FindSystemTimeZoneById | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:204 | ConvertTimeFromUtc | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.TimeZone.cs:223 | GetDaylightChanges | 杩愯鏃舵棤 per-zone 娉ㄥ唽琛?| not-analyzable |
| MethodMap.Type.cs:90 | GetTypeCode | 缂鸿繍琛屾椂 box 绫?鎻忚堪绗︼紙鏈墿鍖?BCL 鍊肩被鍨嬶級 | analyzable |
| MethodMap.Type.cs:116 | Equals | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable |
| MethodMap.Type.cs:163 | GetProperty | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:186 | GetMethod | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:194 | GetField | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:214 | GetMethods | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:222 | GetFields | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:230 | GetProperties | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:238 | GetConstructors | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:251 | GetConstructor | net48 鏃犳閲嶈浇/arity | not-analyzable |
| MethodMap.Type.cs:252 | GetConstructor | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:257 | GetInterfaces | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:272 | GetInterface | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:285 | GetNestedTypes | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:305 | GetNestedType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:323 | GetMembers | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:345 | GetMember | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:357 | MakeGenericType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:372 | GetEvents | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:382 | GetEvent | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:397 | op_Equality | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:488 | GetArrayRank | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:510 | MakeArrayType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:516 | MakeByRefType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:522 | MakePointerType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:541 | GetEnumNames | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:551 | GetEnumValues | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:559 | GetEnumName | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:568 | GetEnumUnderlyingType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:599 | get_IsGenericTypeDefinition | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Type.cs:622 | GetGenericArguments | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:627 | GetGenericTypeDefinition | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:638 | GetTypeFromProgID | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:643 | GetTypeFromCLSID | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:659 | InvokeMember | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:707 | get_AssemblyQualifiedName | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:713 | get_Assembly | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:752 | GetName | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:762 | GetType | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:769 | GetTypes | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:775 | get_FullName | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:781 | GetExecutingAssembly | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:787 | GetEntryAssembly | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:808 | get_Name | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.Type.cs:814 | get_FullName | arity/閲嶈浇褰㈢姸鏈帴绾?| analyzable |
| MethodMap.UIntPtr.cs:70 | CompareTo | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | not-analyzable |
| MethodMap.Uri.cs:69 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Uri.cs:105 | ToString | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.ValueType.cs:497 | GetHashCode | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.ValueType.cs:535 | GetHashCode | 鏃犳硶鍒嗙被锛堢己 TypeDef/杞戒綋锛?| analyzable |
| MethodMap.ValueType.cs:541 | GetHashCode | 鏃犳硶鍒嗙被锛堢己 TypeDef/杞戒綋锛?| not-analyzable |
| MethodMap.Version.cs:52 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Version.cs:109 | GetHashCode | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.WeakReference.cs:44 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.WeakReference.cs:74 | Finalize | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Xml.cs:69 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Xml.cs:146 | get_InnerXml | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Xml.cs:190 | .ctor | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |
| MethodMap.Xml.cs:216 | Create | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable |
| MethodMap.Xml.cs:224 | Create | 渚濊禆杩愯鏃剁被鍨?鏈拷韪疄鍙傦紙鍔ㄦ€佸垎娲撅級 | analyzable |
| MethodMap.Xml.cs:269 | Close | 褰㈢姸鏈帴绾匡紙缂栬瘧鏈熶俊鎭畬鏁达級 | analyzable |

Totals: analyzable=303, not-analyzable=134


## 9. The local ReadOnlySpan<char> variable form (P3-3) -- documented limitation, LOUD at build

The AUDITED-UNSUPPORTED shape:

```csharp
ReadOnlySpan<char> r = "x";
sb.Append(r);
```

The span VALUE local (a generic struct `System.ReadOnlySpan`1<char>`) has no
declaration path: the M6 model materializes no span struct type (the carrier
model represents a span as its BACKING object, and only at the wired call
sites), so a named span local is an unrepresentable slot. Registered as a
documented limitation -- NOT silent.

Measured failure mode (net9 probe, 2026-10-27): the net9 compiler lowers the
string-to-ReadOnlySpan conversion to `call System.String::op_Implicit(string)`
(the ReadOnlySpan<char> operator referenced through the string type). The
String domain has no `op_Implicit` arm, the member falls to the generated-name
fallback, and the emitted call (`System_String_op_Implicit_<hash>(...)`) has no
definition -- the cmake build fails LOUD with
`'System_String_op_Implicit_...' was not declared in this scope` (a
transpile-time emitted phantom, caught at link/layout). No silent wrong value
is possible: the symbol cannot link.

The WHOLE-OBJECT span carrier forms ARE supported and e2e-pinned
(`tests/cases/loud-sbfmt`): `sb.Append(str.AsSpan())` (the string-backed
carrier, section D) and `sb.Append((ReadOnlySpan<char>)charArray)` (the
char[]-backed carrier, section E). The wired arm is
`MethodMap.StringBuilder.cs:119` (`ps[0]==0x15` ->
`icall_stringbuilder_append_span`, the kernel dispatching on the backing
object class) -- it consumes a materialized carrier expression, which a local
span variable never becomes.

## 10. Closure-captured VALUE types -- fixed for the BCL value structs, LOUD for the rest

A captured value-type local of a closure lives in the compiler-generated
NESTED display class (`<>c__DisplayClassN_0`), so its slot is decided by the
NESTED field decode (`TypeEmitter.DecodeNestedFieldSignature`). That decode
collapsed every value type to an int32 slot, so a captured `DateTime` became
`int32_t d;` while the IL value is the 16-byte
`il2cpp::runtime::Il2CppDateTime`: the store (`->d = t1`) did not compile, and
the read (`icall_datetime_get_year(*reinterpret_cast<Il2CppDateTime*>(&->d))`)
silently took 8 bytes out of a 4-byte slot.

FIXED in this batch: the BCL runtime value structs keep their REAL value slot
-- `System.Guid` (16 bytes, align 4), `System.DateTime` (16/8),
`System.TimeSpan` (8/8), `System.Decimal` (16/8) and
`System.DateTimeOffset` (16/8) -- and the class slot grid advances by the
value's own width (a 16-byte member spans two pointer-size slots, so the
following fields and the GC `refFieldBitmap` stay in lockstep). An ENUM keeps
the int32 carrier slot (the D5 / com17type doctrine -- the IR models every
enum value as an int64 constant). e2e lock: `tests/cases/closure-dt` (a pure
and an impure DateTime closure, plus TimeSpan / decimal / Guid closures).

LOUD (transpile-time `NotSupportedException`, never a silently mis-typed
slot): every OTHER value type on the nested path -- a user struct
(`struct Pt`), a `Nullable<T>`, an unknown BCL struct. The nested path has no
runtime value shape for them: the member would have to be the generated C++
struct, whose definition order, size accounting and interior-reference GC
semantics are not wired for the closure layout. The top-level instance path
already rejects the same shapes ("value-type instance fields not supported in
M2", `MetadataLoader.DecodeFieldSignature`), so the nested path now matches
that contract instead of diverging into an int32 slot. Measured corpus impact:
of the 1045 built case assemblies, only 25 nested value-type instance fields
exist -- 23 user-enum fields (still the int32 carrier) and 2 `System.Guid`
fields -- so the rejection has no case-level regression.

FIXED (impl-dec-rvalue, commit 000037aa): a DECIMAL operand that is a
PRVALUE used to be addressed directly -- `f() == 2.5m` where `f` is a
`Func<decimal>` emitted `icall_decimal_equals((&(delegate_result)), ...)`, and
because the decimal icalls take `const Il2CppDecimal*` that address-of landed
on an rvalue ("taking address of rvalue", gcc `-fpermissive`). The "dec"
argument cast (`CodeGenerator.cs`, the `"dec"` arm) was shape-blind: it
assumed every non-address operand was an addressable lvalue slot. The same
arm also MISSED `FieldAddrExpr`, so a closure-captured decimal's
`d.CompareTo(x)` (an ldflda) emitted `&(&x->d)` -- address of an rvalue
pointer, "lvalue required as unary '&' operand".

The cast is now split by shape (`CodeGenerator.DecimalValuePtr`): an ADDRESS
operand (`IsAddressExpr` -- ldloca / ldflda / ldsflda) passes through uncast
(it already renders `&x`); a slot-NAME operand (a local, a static field, an
instance field read) keeps the plain `&(slot)`; every other shape is bound to
a const reference first, which materializes a temporary whose lifetime covers
the full call expression and IS addressable (the `GuidValueBytePtr`
precedent; the parameter is already `const`, so no `const_cast`). The
fix covers the whole decimal icall family, not just comparisons: the same
cast serves `+ - * / %`, unary negation, the `op_Explicit` conversions and
the instance members (CompareTo / Equals / ToString / GetHashCode). e2e lock:
`tests/cases/decrvalue` (delegate-Invoke operands on both sides, a constant on
the left, zero/negative boundaries, arithmetic on prvalues, conversions, a
captured-decimal ldflda, a div-by-zero error path and the lvalue control).

The prvalue shape the fold actually produces is a decimal-returning delegate
Invoke -- every other decimal-producing call is assigned to its result slot
first, and `OptimizeTemps` skips the value-slot ResultKinds (dec/guid/ip4).
`tests/cases/closure-dt` keeps the comparison INSIDE the closure (the operand
is then the captured field, an lvalue). See `tests/cases/guidmethod2` for the
materialized-temporary comparison shape.

Two residuals stay OPEN (both pre-existing, neither hit by the corpus): the
C++ argument evaluation ORDER of two delegate-Invoke operands in one call is
unspecified (net48 is left-to-right; observable only with side-effecting
invokes, and decimal comparison/arithmetic are side-effect free), and the
`decaddr` arm (ToString(IFormatProvider) address receivers) is not extended
to prvalues -- a prvalue receiver routes through the `"dec"` arm instead, and
any future prvalue reaching `decaddr` fails at C++ compile time (loud), never
silently.


## 11. The value-type source family (System.DateTime / System.TimeSpan / System.Guid) -- WIRED on the family kernels, LOUD elsewhere

### 11.1 The defect that started this (byte-vs-name dispatch)

`Enumerable`'s generic-argument dispatch byte is `0x11` for **EVERY** value
type. The LINQ value-type source block used to gate the DECIMAL kernels on
`genArgs[0] == 0x11` alone, so a `DateTime[]` / `TimeSpan[]` / `Guid[]` source
was bound to the decimal kernels (`il2cpp_linq_first_dec` & co): a 16-byte
`Il2CppDecimal` cell read over an 8-byte (`DateTime` / `TimeSpan`) or 16-byte
(`Guid`) payload. That is a SILENT wrong value, and the shipped `zmatrix` case
proved it as a C++ compile error (`zmatrix.cs:692-695/711-712`: "cannot
convert 'il2cpp::runtime::Il2CppDateTime*' to 'il2cpp::runtime::Il2CppDecimal*'").

The NAME is the only authority that can tell the families apart (the byte is
shared), so every value-type gate now asks the generic-argument NAME first
(`IsDecimalSourceNamed`, `MethodMap.Linq.cs:2875`).

### 11.2 What is wired (the family-generic kernels, `src/runtime/include/internal/linq_family.h`)

`linq_family.h` already ships family-generic kernels that read/write the
element at its REAL width when the caller passes the cell code as the trailing
literal -- `IL2CPP_LINQ_CELL_DATETIME` = 12, `IL2CPP_LINQ_CELL_TIMESPAN` = 13,
`IL2CPP_LINQ_CELL_GUID` = 14 (`IL2CPP_LINQ_CELL_DECIMAL` = 11,
`IL2CPP_LINQ_CELL_REF` = 15). The transpiler now routes the value-family
sources through them (`MethodMap.Linq.cs:1069-1148`, cell code resolved by
`LinqValueFamilyCellCode`, `:2924`):

| Operator | Kernel | Notes |
|---|---|---|
| `Take` / `Skip` / `Reverse` | `il2cpp_linq_take_family` / `_skip_family` / `_reverse_family` | fresh array, the cell code as the trailing literal |
| `Concat` / `Distinct` / `Except` / `Union` / `Intersect` | `il2cpp_linq_concat_family` / `_distinct_family` / `_except_family` / `_union_family` / `_intersect_family` | same |
| `DefaultIfEmpty(source)` | `il2cpp_linq_default_if_empty_family` | the 2-argument form has NO family entry -- stays LOUD |
| `LongCount` | `il2cpp_linq_long_count_family` | int64 result |
| `SequenceEqual` | `il2cpp_linq_sequence_equal_family` | |
| `Contains(value)` | `il2cpp_linq_contains_family_dt` / `_ts` / `_guid` | the VALUE entry: the cell code is implied, NO literal |
| `ElementAt` | `il2cpp_linq_element_at_fam_dt` / `_ts` / `_guid` | the element crosses BY VALUE |
| `Single` / `SingleOrDefault` | `il2cpp_linq_single_fam_*` / `il2cpp_linq_single_or_default_fam_*` | BY VALUE |

The by-value `Guid` return needed a new result kind: the pre-existing `guid`
kind is the Guid icall OUT-BUFFER ABI (`guid.h` writes through a byte
pointer), while the family kernels RETURN the value -- hence `guidv`
(`CodeGenerator.cs:3279-3290`, the ExprKind mapping at `:5678`).

E2E proof (byte equality with the net48 oracle):
`tests/cases/loud-linq-vtfam` -- `ok=True`, oracle sha = runtime sha =
`278d62455e6f769d9f7b0b433568e6191d86fe4bcb5ccd620dbe0d4bc6276479` (31
printed lines: Take / Skip / Reverse / Distinct / Concat / Except / Union /
Intersect / DefaultIfEmpty / LongCount / SequenceEqual / Contains /
ElementAt / Single / SingleOrDefault over `DateTime[]`, `TimeSpan[]`,
`Guid[]`).

### 11.3 Registered gaps (the shapes that stay LOUD -- never silent)

1. **The predicate family over a value-family source.** `Where`, `All`,
   `TakeWhile`, `SkipWhile`, `Count(predicate)`, `Any(predicate)` have NO
   DateTime / TimeSpan / Guid kernel in the runtime (`linq.h` ships the
   predicate entries per i4 / ref / i8 / d / f / narrow family only), and
   `First` / `Last` have no family entry at all. The transpiler LOUDs them
   with the source name. **Change surface**: per-family predicate entries in
   `linq.h` (a `Func<DateTime,bool>` by-value ABI) plus the matching
   `InvokeSel*` selector ABIs.
2. **A USER STRUCT source.** `LinqValueFamilyCellCode` returns null for any
   name outside `System.DateTime` / `System.TimeSpan` / `System.Guid` -- a
   user struct has no cell ABI, so the operator stays LOUD (the decimal
   kernels are NOT used as a fallback: that was the silent-wrong-value path).
   **Change surface**: a per-struct cell ABI, or a boxed-element family.
3. **`DefaultIfEmpty(source, defaultValue)`.** Only the source-only form has a
   family entry; the 2-argument form needs a by-value default parameter per
   family.
4. **The grouping-bucket operators over a non-decimal value-type bucket**
   (the byte-gate family of the bucket arms -- see 11.4).
5. **`IsDecimalSourceNamed`'s permissive fallback.** When the generic-argument
   NAME table could not be decoded (`genArgNames == null` or the slot is
   `"?"`), a `0x11` source is treated as **decimal** (the legacy behaviour the
   unnamed-decimal unit test depends on). A source whose name is unknown
   therefore still rides the decimal kernels. **Change surface**: refuse the
   decimal kernels when the name is unknown and LOUD instead -- that needs the
   name table to be reliable first (a tracked-array-name fallback exists for
   the value-family path, `LinqValueFamilyCellCode`).
6. **The tracked-array-name fallback.** `LinqArrayTypeName(0x11)` returns
   `"System.Decimal[]"` for EVERY value-type array (the byte-only helper), so a
   `DateTime[]` snapshot used to be tracked as a `decimal[]` and the downstream
   operators resolved the decimal family against it. The array-source `ToArray`
   and the bucket `ToArray` now use the name-aware `LinqArrayTypeNameExact`
   (`MethodMap.Linq.cs:2146`, `:3448`). **Residual**: the grouping bucket's
   element array is a BOXED reference array (the b4 decimal-source design), so
   the bucket `ToArray` returnTypeName is exact only for the decimal bucket;
   for a value-family bucket the snapshot is `object[]` (the bucket cell
   operators are LOUD anyway -- 11.4).
   operators are LOUD anyway -- 11.4).
7. **The THROW paths of the value-family entries are not e2e-pinned yet.**
   `tests/cases/loud-linq-vtfam` pins the VALUE paths only (31 records: the
   Take / Skip / Reverse / Distinct / Concat / Except / Union / Intersect /
   DefaultIfEmpty / LongCount / SequenceEqual / Contains / ElementAt / Single /
   SingleOrDefault results). The net48 EXCEPTION semantics -- `Single` over a
   multi-element source (`InvalidOperationException`) and `ElementAt` out of
   range (`ArgumentOutOfRangeException`) -- are guaranteed by the runtime
   kernels, not by the transpiler, but no case asserts them yet. **Change
   surface**: two more records in the e2e case (the throw type + the message
   shape) -- a follow-up, not a functional gap.
8. **The predicate-family LOUD messages are unit-pinned only.** The
   `Where` / `All` / `TakeWhile` / `SkipWhile` / `Count(predicate)` /
   `Any(predicate)` LOUD over a DateTime / TimeSpan / Guid source is asserted
   in `tests/unit/MethodMapTests.Linq.cs` (the message text), but no e2e case
   exercises the LOUD build path for these shapes (a LOUD shape cannot reach
   the runtime, so the e2e would assert a transpile-time failure). Accepted:
   the unit pin is the only observable surface.

### 11.4 The grouping-bucket byte gate (`:142` / `:528`) -- reachable, and now LOUD

The bucket arms (the operators over an `IGrouping<TKey,TElement>`) gated the
DECIMAL bucket kernels on `genArgs[0] == 0x11` as well. A `DateTime` / `Guid`
bucket has NO bucket kernel in the runtime: the decimal bucket entries read
16-byte decimal cells and the int32 bucket entries 4-byte cells out of the
bucket's own 8/16-byte payload -- both a SILENT wrong value.

**The arms ARE reachable**: a `DateTime[]` / `Guid[]` source GroupBy keeps its
value-type key path by NAME (`GroupBy` over a value-type source), and the
resulting bucket is an `IGrouping` with a value-type `TElement` -- so
`bucket.Where(...)`, `bucket.First()`, `bucket.ElementAt(...)`,
`bucket.Count(predicate)`, `bucket.Any(predicate)`, `bucket.All(...)`,
`bucket.Take(...)`, `bucket.Distinct()`, `bucket.Sum()` all land in the
bucket arms.

Current state: the bucket arms now gate the decimal entries by NAME
(`decBucket` / `valueBucket`, `MethodMap.Linq.cs:528-530`); the
family-agnostic entries (the header-only `grouping_get_count` /
`grouping_any`, and the `ToArray` snapshot) keep working, and every
cell-reading operator over a non-decimal value-type bucket LOUDs with the
bucket element type named (`MethodMap.Linq.cs:768-784`). The aggregate arm
(`:142-146`, `:227-234`) LOUDs the same way.

**The correct fix** (not done here): the bucket must carry its ELEMENT
FAMILY (the `GroupElemKind` tag the runtime already stores per bucket --
`linq_map.cpp`), and the runtime must grow bucket entries that read the
bucket cells at the family's own width. A transpiler-only fix is NOT
possible: the bucket's cell width is a runtime fact.

**Not silent**: transpile-time LOUD naming the bucket element type.


### 11.5 impl-aggvt: Aggregate with a DECIMAL accumulator over the value-cell sources -- WIRED (dt/ts/guid), LOUD (user struct)

The `Aggregate(source, seed, func[, resultSelector])` arms gated their
LOUD on `!decSrc && IsLinqValueTypeSource(...) &&
IsDecimalSourceNamed(...)` -- a condition that is FALSE for every
`DateTime` / `TimeSpan` / `Guid` source (`IsDecimalSourceNamed` answers
false for exactly those names), so the gate could never fire and the
shape fell through to the INT32-reader decimal kernels
(`FoldDecimalAccI4Src`, the `DecAccFn` `Il2CppDecimal(*)(Il2CppDecimal,int32_t)`
ABI): an 8-byte TimeSpan cell read as a 4-byte slice (the net48
`TimeSpan[]{1d,2d}.Aggregate(0m,(acc,t)=>acc+t.Days)` fold answered 0
instead of 3) and the packed DateTime cell read through a stale pointer
(0xC0000005 -- UB, optimization-dependent, never assert "merely does not
crash").

The fix (`MethodMap.Linq.cs` Aggregate arm, count==3/4):
`LinqValueFamilyCellCode` selects the kernel family ("12"/"13"/"14");
only an unresolvable 0x11 name (a user struct) keeps the LOUD. The
runtime grows `il2cpp_linq_aggregate_seed_{dt,ts,guid}_dec` (the T3
decimal contract: result slot first, seed by pointer) plus the six
`IL2CPP_LINQ_AGG_DEC_RS` `_rs` projections per family; each fold reads
the element at the SOURCE ARRAY's own cell width (`LinqRequireCell`
12/13/14 -> 8/8/16 B) and crosses it BY VALUE (`Il2CppDateTime` /
`Il2CppTimeSpan` / `Il2CppGuid`) -- never the 16-byte decimal stride.

E2E proof (byte equality with the net48 oracle): `tests/cases/agg-vtsrc`
-- `ok=True`, oracle sha = runtime sha =
`19fe5585ac0c5f370fed56de432b506851920b9b67b7ccf7d45f523ad75c6e68`
(TimeSpan negative values + Days, empty-source seed, DateTime
cross-month Day/Month, Guid, and the count==4 forms with
TResult=int32/decimal).

**Newly-registered pre-existing defect (fixed by 044eaefe; it blocked
branchy decimal lambda bodies):** a DECIMAL branch-merge phi temp was
typed `int32_t` when a lambda body branches around a decimal value
(e.g. `(acc, g) => acc + (g == Guid.Empty ? 1m : 100m)`: Roslyn
duplicates the int->decimal conversion INTO each branch, so the merged
`acc` copy is a decimal stack value, but the emitted temp was declared
`int32_t` -- `t1 = arg1;` -- and `icall_decimal_add` then receives an
`int32_t*`: a C++ compile error (a C0 build failure, NOT a wrong-value
risk). The same shape compiles and runs correctly when the fold is
branch-free (the ternary value converts AFTER the merge, or the body
uses straight-line arithmetic). **Root-cause site (corrected -- the
original registration blamed the stack-fold, which is WRONG):** the
parameter kind seed loop in `CodeGenerator.EmitBody` (~:2550-2564) knew
only the Guid value shape, so the phi propagation chain through
`ExprKind(LocExpr)` fell to the "val" default and the merged slot was
declared `int32_t`. **Fixed** by commit 044eaefe (= 257b860f): the seed
loop adds the four wide-value-type seeds (dec/dto/dt/ts) and BCL enum
parameters keep the int32 carrier; the `decphiprobe` e2e regression
(this shape) is byte-equal with the net48 oracle.


## References (section 11)

- Value-family arm: `src/il2cppc/Emit/MethodMap.Linq.cs:1069-1148`; cell
  code helper `:2924-2949`; the decimal-name authority `:2875-2890`.
- Bucket gates: `src/il2cppc/Emit/MethodMap.Linq.cs:142-146` (aggregate),
  `:227-234` (aggregate LOUD), `:528-530` (bucket), `:768-784` (bucket LOUD).
- Name-aware array tracking: `src/il2cppc/Emit/MethodMap.Linq.cs:2146`
  (array-source `ToArray`), `:722-726` (bucket `ToArray`), `:3448`
  (`LinqArrayTypeNameExact`).
- By-value Guid result kind: `src/il2cppc/Emit/CodeGenerator.cs:3279-3290`,
  `:5678`.
- Runtime cell ABI: `src/runtime/include/internal/linq_family.h` (the
  `IL2CPP_LINQ_CELL_*` codes and the family kernels),
  `src/runtime/src/linq_family.cpp`.
- Tests: `tests/unit/MethodMapTests.Linq.cs` (the value-family theories and
  the bucket LOUD theories), `tests/cases/loud-linq-vtfam` (e2e, byte-equal
  with net48), `tests/cases/zmatrix` (the compile-error regression).

## 13. Array-initializer / BCL value-element residuals (impl-arrayinit)

Registered by the InitializeArray + ldelem/stelem value-element batch. Two
pre-existing shapes stay outside that batch (NOT silently wrong -- they still
reject loudly at transpile time):

- **Multidim arrays of value structs / enums.** ~~`new DateTime[2,2]` (and
  Guid/DateTimeOffset/Decimal/any enum) is rejected at the `newobj` allocating
  ctor: `ReadMultidimElemSuffix` sees ELEMENT_TYPE_VALUETYPE (0x11) and throws
  `multidim array element byte 0x11 (VALUETYPE) in M3 (allocation)`
  (`src/il2cppc/Ir/StackFold.cs`). The multidim allocator
  (`il2cpp_array_new_multi_*`) only carries the M3 primitive suffix table, so
  closing this needs a value-element width/kind channel plus runtime support;
  out of scope here.~~ CLOSED (impl-multidimvt, commit 8c123d2b): the
  allocation layer maps the 0x11 element through the value channel
  (DateTime/TimeSpan 8-byte packed-image cells, Guid/Decimal/DateTimeOffset
  16-byte value-struct cells, an enum its UNDERLYING primitive from the raw
  value__ signature) and the runtime gained the typed rank-fixed accessors
  (`il2cpp_array_md_vt.h`) plus the Address element-pointer form. Residuals
  (all still LOUD, never silent): (a) a user STRUCT with possible reference
  fields keeps the 0x11 reject -- the GC reference-field bitmap channel is
  not modeled; (b) a nested value GENERIC (0x15 marker 0x11) keeps its
  reject; (c) the boxed-object Array surface on a multidim enum array
  (`GetValue/SetValue` through System.Object) trips the enum-box unbox wall
  (`Specified cast is not valid: object is not a boxed Int32`) -- the object
  boxing surface, not the multidim channel; (d) a pre-existing fold-stack
  imbalance in the `ldloca; call Guid::.ctor(string)` address-init shape
  (the `il2cpp_guid_ctor_string` mapping carries rv=true, so the fold pushes
  the value the IL never leaves -- 4f1cde64) surfaces as a join-height error
  when a loop/branch follows; test cases there use `Guid.Parse`.
- **Enum array cell stride.** `ArrayElementType` currently allocates every
  user-enum SZARRAY with the pointer-size fallback (8-byte cells). That is
  self-consistent with csc's `stelem.i1/i2/i4/i8` choice (each store writes at
  the cell start and the reader uses the same width), so enum arrays are
  CORRECT today but waste bytes; a byte/long-underlying compact stride would
  also have to teach the Array element-size consumers, so it is deferred.
- **RVA blob shapes not emitted by net48 csc.** For C# source, csc emits
  `RuntimeHelpers.InitializeArray` only for `>= 3` constant elements of a
  primitive/enum element type; `Guid[]/DateTimeOffset[]/DateTime[]/TimeSpan[]/
  decimal[]` literals are always element-wise `stelem <T>` (ILDASM probe). The
  width-16 Guid/DateTimeOffset writers added by this batch are therefore
  reachable only from hand-written/synthetic IL and are covered by the
  synthetic-PE unit tests in `tests/unit/StackFoldTests.InitializeArray.cs`.
## 12. E-cluster: interface slot resolution -- the `InterfaceSlot` guard and the dispatch ABI

### 12.1 The assigned question: is the `InterfaceSlot` throw reachable?

`InterfaceSlot(TypeDef iface, string sig)` (`src/il2cppc/Emit/CodeGenerator.cs:6919`)
throws `NotSupportedException("interface ... slot not found")` when no
`iface.VirtualMethods[i]` satisfies `VTablePlanner.SigKey(m) == sig`.

**Judgement: NOT reachable from Roslyn/net48-emitted metadata. The throw is a
defensive assertion, not a live code path.**

Why the two operands cannot diverge:

- `sig` is built by `VirtualTarget` (`CodeGenerator.cs:6971`) as
  `$"{methodName}({paramTypes})"`, where `paramTypes` is
  `MethodSignatureCoder.Decode(md, <call-site token>.Signature)`.
- `SigKey(m)` is `$"{m.Name}({m.ParamTypes})"` (`VTablePlanner.cs:269`) and
  `MethodDef.ParamTypes` is `MethodSignatureCoder.Decode(md,
  mdInfo.Signature)` on the interface's OWN MethodDef (`MetadataLoader.cs:183`).
- Both sides run the SAME decoder over the SAME blob: Roslyn copies the
  interface method's signature into the MemberRef verbatim (a generic argument
  is NOT substituted -- VAR/MVAR stay 0x13/0x1E and decode to the same "i"), and
  custom modifiers are normalized away on both sides by
  `SkipCustomModifiers`. Every shape the task listed (int32 / decimal /
  DateTime / `List<T>` / `ref` / `out` / explicit implementation / closed
  generic interface / inherited interface) therefore produces identical keys --
  MEASURED: the e2e probe below never fired the guard.

For the guard to fire one of these would have to hold; none is reachable from
C# on net48, and each is registered here instead of being guessed at:

1. a MemberRef whose parent is a DERIVED interface while naming an INHERITED
   method. Roslyn always parents the call at the DECLARING interface, and an
   interface has no `BaseType`, so `Plan` never copies a parent interface's
   slots into a child interface's `VirtualMethods`
   (`VTablePlanner.cs:200-205`: `slots` starts from `t.BaseType` only).
2. a hand-written/obfuscated module whose MemberRef signature blob differs from
   the MethodDef blob in a shape the decoder PRESERVES (modreq/modopt are
   stripped, so `in`/volatile differences do NOT diverge).
3. a `MethodSpec` token: `VirtualTarget` would fail earlier on
   `(MemberReferenceHandle)handle` (`CodeGenerator.cs:7051`). MEASURED with a
   throwaway `IGenMethod<T>.Echo<T>` probe: the CALLING body is emitted as a
   LOUD stub (`// NOT TRANSPILED: P::Main`, `CodeGenerator.cs:1287-1293`), so
   the call never reaches `InterfaceSlot`.

Category: neither (c) "kernel missing" nor (d) "needs runtime type" -- it is a
LOUD-only invariant guard whose REACHABLE sibling failure is the `-1` slot map
(`il2cpp_interface_dispatch` abort). Per the batch rules no statically
analysable shape was converted to LOUD and the guard is left in place.

### 12.2 What the probe DID expose: 3 reachable defects (fixed)

Probe: `tests/cases/ifslotprobe` -- interface + implementation class, every call
goes through an interface-typed variable (so the S3/S1 devirtualization gates
that hide the bug behind a single implementer + static receiver do not apply).

RED (same probe, before the fix):

- transpile warnings `interface IGen`1::Echo(i) not implemented by GenStr
  vtable -- skipping` and `interface IExplicit::Value(i) not implemented by
  ExplicitImpl vtable -- skipping` -> the emitted slot maps carry `-1`
  (verified in the generated `GenStr_iface_IGen_1_int_map` /
  `ExplicitImpl_iface_IExplicit_map`), and
  `il2cpp_interface_dispatch` aborts "slot map out of range"
  (`src/runtime/src/metadata.cpp:744-748`).
- the generated C++ does not compile (dispatch fn-pointer cast != callee
  prototype):
  - `error: no match for 'operator=' (operand types are
    'il2cpp::runtime::Il2CppDecimal' and 'Il2CppObject*')` -- a decimal RETURN
    dispatched through an interface-typed receiver;
  - `error: invalid conversion from 'int32_t*' to 'int32_t'` -- a `ref int`
    PARAMETER of an interface method.

Root causes and fixes (all three are signature/ABI MIRRORS -- `SigKey`, the C++
symbol hashes and the runtime helper contract are untouched):

1. `VTablePlanner.BuildInterfaceMaps` resolved interface slots by exact
   `SigKey` only (`VTablePlanner.cs:128-129`). Two legitimate spellings fall
   outside it: an EXPLICIT implementation (the class method is named
   `<InterfaceFullName>.<Method>`), and a GENERIC interface closed with a
   concrete argument (the interface slot spells T as the VAR default `i`, the
   class method carries `r:System.String`). New `ResolveInterfaceImplSlot`
   (`VTablePlanner.cs:160`) resolves both and requires UNIQUENESS -- an
   ambiguous candidate set still yields `-1` (loud abort), never a guess.
2. `VirtualParamTypesCpp` (`CodeGenerator.cs:6876`) declared the dispatch cast's
   parameters with an `int32_t` default for every encoding other than `r:`/`V:`.
   It now mirrors `ParamTypesCpp` (the callee prototype) arm for arm:
   `B:` -> `CalliParamCpp` (the ADDRESS: `int32_t*`, `Il2CppObject**`,
   `Struct*`), `d`/`f` -> `double`, `l`/`q` -> `int64_t`, `n` -> `intptr_t`,
   `F:` -> `void*`, a known BCL enum -> `int32_t`. VAR slots keep the `int32_t`
   default instantiation (`CallSiteVarIsRef` still widens reference
   instantiations afterwards) -- no generic ABI drift.
3. `VirtualReturnTypeCpp` (`CodeGenerator.cs:7007`) returned `Il2CppObject*`
   for every VALUETYPE return while the definition side declares the runtime
   struct BY VALUE (`TypeCppName(ManagedReturnStructName)`). New
   `VirtualReturnCpp` (`CodeGenerator.cs:7028`) names the struct, and
   `VirtualReturnResultKind` (`CodeGenerator.cs:7037`) now seeds the
   `dt`/`ts`/`dto`/`bi` result-temp kinds so the temp declaration matches too.

GREEN: `ok=True`, oracle sha == runtime sha
`d61a791fc78388ef1367ac7ab38ac0558c0bdeede9e0a3639d09c7747bd19b2b`
(17/17 records byte-identical).

### 12.3 Residuals (registered, NOT fixed here)

- STRUCT implementer boxed into an interface (`IBaseline sb = new StructImpl()`)
  emits `&StructImpl_class`, but a value type has no class descriptor (only
  interface/class descriptors are emitted) -> C++ "`StructImpl_class` was not
  declared". Needs a boxed-value descriptor for structs (value-struct domain,
  outside this batch); the probe therefore uses class implementers only.
- `string.Join(string, IEnumerable<int>)`: net48 binds the IEnumerable overload,
  the resolver maps it to `icall_string_join`, which reads the receiver as a
  string ARRAY -> empty output (and, with redirected stdout, a buffered-crash
  with no visible records). Unrelated BCL carrier gap; the probe reads
  `mk.Count`/`mk[i]` instead.
- a class implementing the SAME generic interface twice with different
  arguments (`IGen<int>` + `IGen<string>`): `InterfaceSlotMaps` is keyed by the
  interface TypeDef, so only one map can exist, and the VAR-tolerant match also
  returns `-1` on two same-name/same-arity candidates -> loud abort. Pre-existing
  model limitation.
- explicit implementation of a NESTED interface (`Outer+Inner.M` name prefix) is
  not matched by `IsExplicitImplementationName` -> stays `-1` (loud).
- a generic METHOD on an interface makes the calling body a LOUD stub
  (`// NOT TRANSPILED`), measured; it never reaches `InterfaceSlot`.

### 12.4 Verification (this batch)

- C0: `dotnet build src\il2cppc\il2cppc.csproj -c Debug --no-incremental` ->
  0 errors, 0 warnings.
- Unit: `dotnet test tests\unit\unit.csproj -c Debug` -> 6 failures, the
  IDENTICAL baseline list (fixture binaries not built):
  `StackFoldTestsInitializeArray.Fold_InitializeArray_{RealBase64Case_ThreeByteStelems,RealBase64Case_FiveByteStelems,RealArrayExt2_Int32Stelems,MissingPe_ThrowsLoudWiringGap}`,
  `StackFoldTests.Fold_ClosureReturn_MakeAdder_BindsDisplayClassAsDelegateTarget`,
  `StackFoldTests.Fold_ClosureMethod_B__0_ReadsCapturedFieldViaThis`.
- difftest (mingw, oracle sha == runtime sha):
  `ifslotprobe` d61a791f..., `zmatrix` 24ba1e99..., `closure-dt` b6a17362...,
  `decimalcase` e2df5e1c...; interface/vtable impact set:
  `genericiface` 04449890..., `genericvirtstr` 023db488...,
  `ilistException` 3dae4d13..., `myIList` 6008a833..., `virtbench`
  0f6fde7c..., `com17type` cbd03919..., `nestiface1` 040e184b...,
  `virtual` 0d40abb6... -- all `ok=True`.

## 14. FIXED (impl-floatkey): the float (0x0C) sort-key family shared the double kernel ABI

Registered from the impl-thenbyfam seat as a SUSPECTED defect:
`LinqKeyFamilyCode` folded float (0x0C) and double (0x0D) into the SAME
key family code "2" (`src/il2cppc/Emit/MethodMap.Linq.cs`), and the
family-2 runtime leaf invoked the key selector through `InvokeKeyI4D`,
typed `double (*)(int32_t)` (`src/runtime/src/linq_sort.cpp`) -- both for
the primary OrderBy key and for the fused ThenBy composite. A
`Func<int,float>` leaf returns a 4-byte float per the managed ABI, so
reading that return through a `double` fn-pointer is an ABI misread.

**probe: REPRODUCED -- real defect (measured, then FIXED).** The
registered e2e case `thenby-floatprobe` (int32[] source x float /
double-key controls x OrderBy / OrderByDescending / fused ThenBy /
ThenByDescending x 4 direction combinations x duplicate keys, plus the
wired int64[]-source float surface) ran RED against the net48 oracle on
the unfixed tree: 15 of 18 records differed (`ok=False`,
sha oracle 4154264a... vs runtime cc28018c...), EVERY differing record
involved a float key and every double-key control matched. The runtime
ordered float keys by the UNSIGNED 32-BIT BIT PATTERN of the float return
read as the low half of a (zero-residue) double: `-1.5f` (0xBFC00000)
sorted LAST ascending instead of first -- the exact ABI misread shape
predicted. The selector exactly-once contract held (11 calls for n=11);
only the key VALUES were misread.

Fix (impl-floatkey): the float family got its own reader and code.
- Runtime: `InvokeKeyI4F` = `float (*)(int32_t)` (+ instance form) and
  `InvokeKeyI8SrcF` = `float (*)(int64_t)`; new entries
  `il2cpp_linq_order_by_key[_desc]_i4_f` and
  `il2cpp_linq_order_by_key[_desc]_i8_f` (`SortInt32KeyCopyT<float>` /
  `SortInt64KeyCopy<float>` + `CmpKeyValue<float>`).
- Numeric fused composite: `LinqValKeyFamilyCode` (NEW -- the numeric
  code space 0=int32, 1=int64, 2=double, 3=float) feeds the ThenBy
  marker literal; the val_i4 / val_i8 dispatch matrices grew from 3x3=9
  to 4x4=16 (k1fam, k2fam) combinations, the float row/column reading
  through `InvokeKeyI4F` / `InvokeKeyI8SrcF`. The DECIMAL composite's
  own `LinqKeyFamilyCode` space (3=decimal, 4=DateTime, 5=Guid) is
  untouched.
- StackFold: primary suffixes `_i4_f` / `_i8_f` map to primary code
  "3"; the secondary literal gate accepts "3".
- Unit: 7 new MethodMapTestsLinq assertions (red 7/519 before, green
  526/526 after); full suite 3835/3835 (baseline 3828 + 7).
- e2e GREEN: `thenby-floatprobe` ok=True, byte-identical (same sha);
  regressions `thenbyfamprobe` edc19205, `thenbydescprobe` ef9c6524,
  `linqorder2` 6a72ece6, `zmatrix` 24ba1e99 all ok=True unchanged.

Residual (registered, NOT probed here): the same float-through-double
fold remains live on three surfaces this probe cannot reach -- (a) a
float SECONDARY key on the DECIMAL fused composite (the dec marker still
carries LinqKeyFamilyCode's "2"=double for 0x0C), (b) reference-element
sources (`il2cpp_linq_order_by_key_ref_d` reads a Func<T,float> key
through InvokeKeyRefD), (c) the List<T> receiver entries
(`il2cpp_linq_list_order_by_key[_desc]_d`). None has e2e coverage today;
they need their own float readers/wiring before they can be called
correct. Also re-confirmed LOUD: `double[]`/`float[]` ELEMENT sources
have NO OrderBy(keySelector) at all (transpile-time reject), so no
silent path hides there.

### 14.1 FIXED (impl-orderfam): the three float-through-double residuals + the wide element-source OrderBy surface

The three 搂14 residuals each got their own float reader and entry, and
the wide element-source OrderBy surface was wired (RED鈫扜REEN, measured).

- (a) DEC fused composite: `LinqKeyFamilyCode` now carries 6=float
  (the pre-existing 0..5 codes keep their meaning: 3=decimal,
  4=DateTime, 5=Guid); the runtime `SortDecTwoKeysDispatch` matrix grew
  6x6 -> 7x7, the float row/column reading through the NEW
  `InvokeKeyDecF` = `float (*)(const Il2CppDecimal&)`. The DIRECT
  decimal-source float key (previously folded into `_dec_d`) rides the
  NEW `il2cpp_linq_order_by_key[_desc]_dec_f`
  (`SortDecKeyCopyT<float>`); the decimal bucket surface mirrors it
  (`grouping_order_by_key[_desc]_dec_f`).
- (b) REFERENCE sources: `LinqKeyFamilySuffix` now maps 0x0C -> "f";
  the NEW `InvokeKeyRefF` = `float (*)(Il2CppObject*)` behind
  `il2cpp_linq_order_by_key_ref_f` (`SortRefsKeyCopy<float>`).
- (c) List faces: `List<double>` + float key rides the NEW
  `il2cpp_linq_list_order_by_key[_desc]_f` (snapshot -> the array
  `_d_f` entry); a reference-element list + float key rides the NEW
  `il2cpp_linq_list_order_by_key_ref_f`. The emission points already
  existed (verified, `MethodMap.Linq.cs` List arm) -- the float key
  was re-pointed, the double key keeps the pre-existing `_d` entry.
- (B) double[]/float[] ELEMENT-source OrderBy(keySelector): previously
  LOUD ("stage-1 runtime LINQ surface is int32 (0x08) element-family").
  NEW `SortWideElemKeyCopyT` (the element snapshot keeps the SOURCE's
  cell width and elem kind; the result is a fresh array of the source's
  family) behind one entry per (source, key) family pair:
  `_d_i4 / _d_i8 / _d (the pre-existing double-key entry) / _d_f` and
  `_f_i4 / _f_i8 / _f_d / _f_f`. The element crosses in the F-stack
  slot (a float element WIDENS to double -- the InvokePredF / FPredFn
  convention); a float key reads through the float(*) reader. The
  transpiler gates by source NAME: only System.Double[] /
  System.Single[] sources lift the int32-element rejection, and only
  for the numeric key families (i4/i8/d/f); a reference / struct key
  over a wide source keeps a precise LOUD, and every other element
  family keeps its existing gate. ThenBy over a wide source stays
  LOUD (out of scope here).
- Unit: 25 new MethodMapTestsLinq assertions; full suite 3860/3860
  (baseline 3835 + 25). C0: il2cppc (dotnet Debug) 0 errors / 0
  warnings; runtime (mingw Release) 0 errors.
- e2e RED->GREEN (mingw, oracle sha == runtime sha):
  `thenby-decfkey` (RED diff 11/13 -> sha afc01112...),
  `thenby-refkey` (RED diff 2/4 -> sha 1f21013d...),
  `list-orderkey-float` (RED diff 4/7 -> sha 60cc1818...),
  `orderwide-elem` (RED: transpile LOUD -> sha 94eb7f3e...);
  regressions `thenby-floatprobe` 4154264a, `thenbyfamprobe`
  edc19205, `thenbydescprobe` ef9c6524, `linqorder2` 6a72ece6,
  `loud-linq-decorderbygrp`   e8754117, `zmatrix` 24ba1e99 all ok=True
  unchanged.

  Registered during this seat, NOT probed / NOT fixed here (out of the
  impl-orderfam mandate -- each needs its own RED probe + float entry):
  - the List<T> AGGREGATE seed form folds a float accumulator into the
    double seed kernel (`MethodMap.Linq.cs` Aggregate count>=3 arm:
    `0x0C or 0x0D => "d"` -> `il2cpp_linq_list_aggregate_seed_d`), so a
    `Func<float,float>` fold return is read through a double fn-pointer
    (the no-seed form already has its own `_f` kernel);
  - the calli result-slot fold classifies a float return as the double
    slot (`StackFold.cs` ResultKind `0x0C or 0x0D => "d"`) -- likely the
    house widened-carrier convention, unverified by any probe.

## 17. impl-linqagg: the List<T> Aggregate SEED (element family, accumulator family) matrix + the value-source accumulator families

floatfix (7b0ee161) split the list seed fold for the DIAGONAL pairs only
(K4+d `seed_d_d`, K2+i8 `seed_i8_i8`, K4+f `seed_f_d`, K3+f `seed_f`). Every
NON-DIAGONAL pair still bound the int32-element base kernels
(`seed_i4/seed_i8/seed_d/seed_f`), whose element reader is fixed at int32:

- `List<long>` + int32 accumulator read the LOW 32 bits of each 8-byte cell
  and called the lambda through an int32 element slot (measured: the baseline
  probe aborts with 0xC0000005 on the first fold -- the `int(int,int64)` leaf
  is called as `int64(*)(int64,int32)`);
- `List<float>` + double accumulator paired TWO 4-byte float cells as one
  double (a silent wrong value + an OOB tail read);
- `List<float>` + float accumulator read the float BITS as int32;
- `List<double>` + int32/int64 accumulator read the low 32 bits;
- `List<byte>/List<short>` (1/2-byte cells) were read as int32;
- `List<string>` + a numeric accumulator read a POINTER's low 32 bits;
- `List<decimal>` (16-byte cells) fell into the reference path;
- a decimal/ref ACCUMULATOR was cast to int32 at the call site (a generated-C++
  compile error).

### 17.1 Fixed (impl-linqagg)

`linq_list.cpp` now has one entry per (element family, accumulator family)
pair, named `il2cpp_linq_list_aggregate_seed_<acc>_<elem>`:

  acc  = i4 | i8 | f | d | dec | ref
  elem = i4 (int32-slot, incl. the 1/2-byte narrow/uint kinds, read through
         `ReadI4(kind)`) | i8 | f (4-byte float cells) | d (8-byte double
         cells) | ref | dec (16-byte decimal cells)

Each entry snapshots at the LIST kind, reads the element at the REAL cell
width, and calls the accumulator family's own leaf ABI (narrow/integral
element -> int32 slot; int64 -> int64; float/double -> widened double;
reference -> Il2CppObject*; decimal -> the 16-byte value struct; float
accumulator -> float return with DOUBLE parameters -- the FAccDFn convention;
decimal accumulator -> the 16-byte value return / result slot; reference
accumulator -> the accumulator local is a GC root). `MethodMap.Linq.cs` now
dispatches `seed_<accFam>_<elemFam>`; a decimal ELEMENT list uses the
`il2cpp_list_obj_to_array_dec` snapshot (kind literal 12).

Value-source accumulators (A cluster): `DateTime` / `TimeSpan` / `Guid`
ARRAY sources with an int32/int64/float/double accumulator were routed to the
int32-element base kernels too (4-byte reads out of 8/16-byte cells + an
int32 element slot against a BY-VALUE struct parameter). They now use
`il2cpp_linq_aggregate_seed_{dt,ts,guid}_{i4,i8,f,d}` (declared in
linq_list.h, defined in linq_list.cpp; the cell readers are exported by
linq_family.h). The decimal accumulator family was already wired by
impl-aggvt.

e2e (mingw, oracle sha == runtime sha):
- `probe-linqagg2` (42 records: int/long/float/double/byte/short/string
  elements x int/long/float/double/decimal/string accumulators + empty
  sources + a chained decimal ToList fold) RED at baseline: runtime exit
  0xC0000005, 0 records; GREEN sha eb49da63...
- `probe-aggvt2` (14 records: dt/ts/guid x i4/i8/f/d + empty) RED at
  baseline: exit 0xC0000005, 0 records; GREEN sha 12a416bb...
- regressions unchanged (exact task SHAs): probe-floatagg 3a01e028,
  loud-linq-listagg 48e3fbff, linqorder2 6a72ece6, list-orderkey-float
  60cc1818, zmatrix 24ba1e99.
- Unit: 3902/3902 (baseline 3899 + 3 new mapping assertions); the two
  pre-existing list-seed assertions that encoded the old bare entry names
  (`seed_i4` / `seed_f`) were updated to the two-family names.

### 17.2 Registered, NOT fixed in this seat (with e2e evidence)

1. **`List<T>.Aggregate(seed, func, resultSelector)`** -- the list arm has no
   result-projection matrix (`_rs_*`) and used to emit the 3-argument entry
   while the IL pushed 4 (a stack desync). It is now a transpile-time LOUD
   (`MethodMap.Linq.cs`, Aggregate count==4). The array surface has the
   `_rs_*` entries; the list surface needs one entry per (accumulator, TResult)
   pair.
2. **`List<decimal>` element receiver** -- FIXED by impl-arrayagg (see
   section 18). A `List<decimal>` LOCAL used to be untracked as a List (the
   stack-kind vocabulary had no decimal tag), so `ldec.Aggregate(...)` fell
   into the ARRAY Aggregate arm with a List object reinterpreted as
   `Il2CppArray*` (a wrong value / access violation). Only a CHAINED
   `decimal[].ToList().Aggregate(0m, ...)` kept the tracked
   `List`1_System.Decimal` receiver.
3. **GroupBy key families** (A cluster) -- FIXED by impl-groupbykey (see
   section 23). Evidence (transpile log at the baseline):
   `GroupBy<int32,TimeSpan>` -> "a value-type (System.TimeSpan) key has no
   runtime key carrier"; `GroupBy<int64,DateTime>` -> "the stage-1 runtime
   LINQ surface is int32 (0x08) element-family". The missing pieces were the
   TimeSpan/DateTimeOffset/user-enum key kinds, the non-int32 element-source
   GroupBy surface and three SILENT WRONG-VALUE comparers (decimal by raw
   image, DateTime by kind, reference key into the result selector). A float[]
   source stays LOUD and is registered in section 23.5.
4. **`DefaultIfEmpty(source, defaultValue)`** (non-decimal) -- the mapping
   answered the 1-argument entry and DROPPED the default (a wrong empty-source
   value / stack desync). It is now a transpile-time LOUD for the int32,
   reference, wide/narrow, list and family arms. The per-element-family
   default-value ABI + kernel lives in linq_family.cpp (outside this seat's
   write scope); the decimal 2-arg form is already wired
   (`il2cpp_linq_default_if_empty_dec`).
5. **Zip over a DateTimeOffset source** (A cluster) -- evidence:
   `Enumerable.Zip<valuetype,valuetype,...> not wired: an element family with
   no cell ABI`. The Zip side descriptor has no 16-byte DateTimeOffset code
   (IL2CPP_LINQ_CELL_DTOFFSET exists in linq_family.h but no zip side maps to
   it). A new side code + result family is needed.
6. **Array numeric NON-DIAGONAL Aggregate** -- FIXED by impl-arrayagg (see
   section 18). `long[].Aggregate(0, ...)`, `double[].Aggregate(0L, ...)`,
   `float[].Aggregate(0.0, ...)` used to bind seed_i4/seed_i8/seed_d (int32
   element reads); the ARRAY arm now has its own (element cell, accumulator)
   dispatch (`il2cpp_linq_aggregate_seed_cell_<acc>`).
7. **A by-value VALUE-STRUCT List element** (`List<DateTime>` /
   `List<TimeSpan>` / `List<Guid>` / a user struct) -- no list element storage
   kind exists, so the Aggregate arm rejects it (LOUD) instead of passing a
   reference slot where the lambda takes the struct by value.
8. **A reference / user-struct ACCUMULATOR over a value-type array source**
   (`DateTime[].Aggregate("", (a,x) => ...)`, a user-struct source with a
   numeric accumulator) -- still routes to the int32 reader (a silent wrong
   value / ABI mismatch). Not in the A-cluster list; registered for the next
   pass.

## 18. impl-arrayagg: the ARRAY (element cell, accumulator family) seed matrix + List<decimal> receiver tracking

Fixes items 6 and 2 of section 17.2 (the ARRAY-domain half of the same defect
class b87d225f fixed for the List domain).

### 18.1 The defect (measured)

The ARRAY `Aggregate(seed, func)` arm only had diagonal / one-family kernels:

  seed_i4 / seed_i8 / seed_d / seed_f   element reader FIXED at int32
  seed_i8_i8                            element reader at int64 (diagonal)
  seed_d_d                              element reader at double (diagonal)
  seed_f_d / seed_f_f                   the float accumulator diagonals

Every NON-DIAGONAL (element, accumulator) pair bound one of the int32-element
entries, so it read the wrong number of bytes out of the source cell and/or
called the lambda through a mismatched fn-pointer. Measured classes:

- `long[]` + int accumulator read the LOW 32 bits of each 8-byte cell;
- `double[]` + int/long accumulator read the low 32 bits;
- `float[]` + int/long accumulator read the float BITS as int32;
- `float[]` + double accumulator paired TWO 4-byte float cells as one double
  (and misaligned the tail);
- `byte[]` / `short[]` (1/2-byte cells) were read as int32 (an over-read);
- `string[]` + a numeric accumulator read a POINTER's low 32 bits;
- a decimal accumulator over a non-int32 source used the int32 reader.

### 18.2 Fixed

`linq.cpp` now provides one entry per ACCUMULATOR family that reads the source
element at the cell's REAL width:

  il2cpp_linq_aggregate_seed_cell_i4   int32_t(*)(int32_t, ELEM)
  il2cpp_linq_aggregate_seed_cell_i8   int64_t(*)(int64_t, ELEM)
  il2cpp_linq_aggregate_seed_cell_f    float(*)(double, ELEM)   (F-stack carrier)
  il2cpp_linq_aggregate_seed_cell_d    double(*)(double, ELEM)
  il2cpp_linq_aggregate_seed_cell_dec  16-byte result slot + value-slot seed
  il2cpp_linq_aggregate_seed_cell_ref  rooted accumulator local

The trailing `cell` literal is an IL2CPP_LINQ_CELL_* code (linq_family.h,
APPENDED -- never a consumed IL argument) and ELEM follows it: int32 for
int32/narrow, int64 for i8, double for f/d, Il2CppObject* for a reference cell,
Il2CppDecimal for a decimal cell. `MethodMap.Linq.cs` keeps the historical
entries for the int32 source / diagonal pairs and routes every other pair to
`seed_cell_<accFam>`.

`List<decimal>` receiver tracking (section 17.2 item 2): `LinqListKind` now
uses the NAME-aware `LinqListKindOfElemByte` overload and accepts the
`_decimal` / `_System.Decimal` suffix, answering list kind 12 (the boxed
decimal listobj storage). `StackFold` materializes an `ldfld`/`ldsfld` of a
`List<T>` FIELD into a typed temp so a stored / captured / static List receiver
carries its declared type into objTypes (the LINQ receiver hint).

### 18.3 e2e

`probe-arrayagg` (47 records): int/long/float/double/byte/short/string/decimal
sources x int/long/float/double/decimal/string accumulators + 4 empty sources +
4 `List<decimal>` LOCAL shapes (stored local, static field, method parameter,
closure capture).

- RED at baseline (b87d225f): runtime exit -1073741819 (0xC0000005), 0 records,
  all 47 records `<missing>`.
- GREEN: oracle sha == runtime sha =
  `875acf6e26244e8402b86331620627fe28c1abb5df716832dae91edc28af88ff`.
- Regressions byte-identical: probe-linqagg2 `eb49da63`, probe-aggvt2
  `12a416bb`, probe-floatagg `3a01e028`, linqorder2 `6a72ece6`, zmatrix
  `24ba1e99`, listops `0ed62ea4`, linqcase `2d76fda3`.
- Unit 3908/3908, C0 il2cppc Debug 0 errors.

### 18.4 Still open after this seat

- `List<T>.Aggregate(seed, func, resultSelector)` (section 17.2 item 1) -- the
  List `_rs_*` projection matrix; still a transpile-time LOUD.
- `DefaultIfEmpty(source, defaultValue)` for a non-decimal source -- still LOUD.
- `Array` Aggregate with a resultSelector over a NON-int32 source -- the ARRAY
  `_rs_*` entries are still int32-element readers; the cell matrix covers the
  3-parameter form only.
- A by-value value-struct ARRAY element (user struct) with a numeric
  accumulator -- no cell ABI; stays LOUD.
- A reference accumulator over a NON-narrow non-int32 ARRAY family keeps the
  pre-existing per-source-family ref entries (unchanged here).

## 19. impl-loudvalstruct: the BY-VALUE VALUE-STRUCT DefaultIfEmpty(source, value) forms

Closes section 17.2 item 4 for the System.DateTime / System.TimeSpan /
System.Guid / System.DateTimeOffset ARRAY families (the int32, reference,
wide/narrow and List families were closed by impl-aggrs; the DECIMAL array
form has its own entry since impl-linq2).

### 19.1 The defect (measured)

`Enumerable.DefaultIfEmpty<TSource>(source, defaultValue)` was a transpile-time
LOUD for every value-struct array source, even though the runtime already
carried the cell writers for all four families (`LinqCellReadDt/Ts/Guid`,
`LinqCellDefault`, `LinqNewArray`):

```
warning: method fold failed -> skipped (Cases.AuditD2b::Main:
  System.Linq.Enumerable::DefaultIfEmpty: Enumerable.DefaultIfEmpty<valuetype>
  not wired: the stage-1 runtime LINQ surface is int32 (0x08) element-family
  for this operator ...)
warning: 1 method(s) were not transpiled (see --stub-report)
```

RED evidence: `audit-fix3-d2b` (DateTime[] + Guid[]), `audit-fix3-d2h`
(DateTime[] / TimeSpan[] / Guid[] with empty, single and multi-element
sources). Both had runtime exit 1 with 0 bytes of output against a 297/163-byte
net48 oracle.

### 19.2 Fixed

Four new runtime entries in `linq_list.cpp` (declared in `linq_list.h`,
include-guarded for `guid.h` / `il2cpp_time.h` / `il2cpp_dtoffset.h`):

```
il2cpp_linq_default_if_empty_family_dt (Il2CppArray*, Il2CppDateTime, int32_t cell)
il2cpp_linq_default_if_empty_family_ts (Il2CppArray*, Il2CppTimeSpan, int32_t cell)
il2cpp_linq_default_if_empty_family_guid (Il2CppArray*, Il2CppGuid, int32_t cell)
il2cpp_linq_default_if_empty_family_dtoffset (Il2CppArray*, Il2CppDateTimeOffset, int32_t cell)
```

The default crosses in the struct's own by-value ABI (the same convention the
`contains_family_*` value entries and the element accessors use). The cell is
written at its REAL width -- and DateTime specifically through
`il2cpp_datetime_to_array_bits`, because the array image packs `kind` into the
top two bits of the 64-bit cell while the runtime struct keeps it in its own
member (a plain `memcpy` of the struct would store the wrong image).

`MethodMap.Linq.cs` binds the four entries in the value-family dispatch block
(`DefaultIfEmpty` with arity 2) and keeps a self-describing LOUD for a user
struct (no cell ABI).

### 19.3 e2e

- `audit-fix3-d2b` GREEN: oracle sha == runtime sha = `16508381`.
- `audit-fix3-d2h` (self-authored) GREEN: oracle sha == runtime sha = `c3b071ff`.
- `audit-fix3-d2d` (narrow families + non-diagonal aggregate triples) GREEN:
  `935b78eb`, 16 records.
- Regressions byte-identical: probe-aggrs `fdac4fff`, probe-linqagg2
  `eb49da63`, probe-arrayagg `875acf6e`, probe-floatelem `9245caa6`,
  probe-aggvt2 `12a416bb`, zmatrix `24ba1e99`.
- Unit 3953/3953 (baseline 3946 + 7 new resolver assertions). C0: il2cppc
  Debug build 0 errors, 0 warnings.

### 19.4 Still open after this seat (registered)

- A by-value value-struct LIST element (`List<DateTime>` / `List<TimeSpan>` /
  `List<Guid>`) with DefaultIfEmpty(value) -- there is no LIST storage kind for
  these families (only `IL2CPP_LIST_KIND_DEC`). The transpiler now rejects the
  shape up front instead of emitting an uncompilable call (MEASURED before the
  gate: `cannot convert Il2CppDateTime to Il2CppObject*` in an
  `il2cpp_linq_list_default_if_empty_val_ref` slot). Locked by
  `loud-linq-die-listvt` + scripts/loud-gate.ps1. Defect D of this seat.
- The same list value-struct element gap blocks the OTHER list operators over
  these families (Aggregate / Where / Select / ...). Same root cause.

## 20. impl-loudvalstruct (C): the value-struct TResult of Aggregate(seed, func, resultSelector) over a List

### 20.1 The defect (measured)

The List 4-parameter projection matrix carried only int32 / int64 / float /
double / decimal / reference results, so a result selector returning a BY-VALUE
VALUE STRUCT was a transpile-time LOUD:

```
warning: method fold failed -> skipped (Cases.AuditD2i::Main:
  System.Linq.Enumerable::Aggregate: LOUD whitelist (no runtime descriptor for
  the TResult struct): Enumerable.Aggregate TResult value type over a List is
  not a supported result family ...)
```

RED evidence: `audit-fix3-d2i` (9 records: DateTime / TimeSpan / Guid results
across int32, int64, double, reference and decimal accumulators, plus the
"selector runs once" call-count contract) -- runtime exit 1, 0 bytes, all 9
records `<missing>` against a 298-byte net48 oracle.

### 20.2 Fixed

`linq_list.cpp` / `linq_list.h`: the projection macros (`LST_AGG_RS6`,
`LST_AGG_RS_DEC6`, `IL2CPP_DECL_LST_AGG_RS`) now emit four more results per
(accumulator, element) pair -- `_rs_dt` (Il2CppDateTime), `_rs_ts`
(Il2CppTimeSpan), `_rs_guid` (Il2CppGuid), `_rs_dtoffset`
(Il2CppDateTimeOffset). They ride the SAME by-value register return the family
element accessors already use, so no new ABI was needed. The matrix is now
6 accumulator families x 6 element families x 10 result families = 360 entries.

`MethodMap.Linq.cs`: the 4-parameter arm resolves a 0x11 TResult through the
decoded generic-argument NAME (decimal first, then the four structs) and binds
the corresponding entry with the struct's result kind (`dt` / `ts` / `guidv` /
`dto`). A USER struct still LOUDs (no result ABI).

The ARRAY surface deliberately keeps its LOUD: it has no value-struct result
projection, and adding one is a different (cell-matrix) surface. The HINT-LESS
first pass of an aggregate cannot see the source type (its arguments leave the
source below the stack top, so `arg0Type` is the LAMBDA), so it must not throw
-- it answers the decimal-carrier entry and StackFold's post-pop re-resolve
dispatches the List domain. A source PROVEN to be an array (`name[]`) keeps the
LOUD. This is the same deferral defect A needed.

### 20.3 e2e

- `audit-fix3-d2i` GREEN: oracle sha == runtime sha = `f5d029ee`, 9 records.
- Regressions byte-identical: probe-aggrs `fdac4fff`, probe-linqagg2
  `eb49da63`, probe-arrayagg `875acf6e`, probe-floatelem `9245caa6`,
  probe-aggvt2 `12a416bb`, zmatrix `24ba1e99`.
- Unit 3960/3960 (baseline 3946 + 14 new resolver assertions). C0: il2cppc
  Debug build 0 errors.

## 21. impl-loudvalstruct (D): the by-value VALUE-STRUCT List element

### 21.1 The defect (measured)

A `List<T>` whose element is a BY-VALUE VALUE STRUCT (`List<DateTime>` /
`List<TimeSpan>` / `List<Guid>` / `List<DateTimeOffset>`) had NO list storage
kind. `LinqListKindOfElemByte` answered null for every 0x11 name except
`System.Decimal`, so the family fell into the REFERENCE tag
(`IL2CPP_LIST_KIND_REF`) and the listobj snapshot handed out the BOXED POINTER
while the lambda takes the struct BY VALUE. Measured on `audit-fix3-d2b`:

```
error: cannot convert 'il2cpp::runtime::Il2CppDateTime' to
       'il2cpp::Il2CppObject*'
  in: il2cpp_linq_list_default_if_empty_val_ref(Il2CppObject*, Il2CppObject*, int32_t)
```

RED evidence: `audit-fix3-d2j` (empty / single / multi element DateTime lists,
TimeSpan and Guid lists, plus the non-empty pass-through) -- a hard cmake build
failure, 0 bytes of output against a 351-byte net48 oracle.

### 21.2 Fixed

`linq_list.h` gained `IL2CPP_LIST_KIND_DT = 13`, `_TS = 14`, `_GUID = 15`,
`_DTOFFSET = 16` (12 was the decimal kind). `linq_list.cpp`:

- `SnapshotOf` maps the four kinds to the EXISTING listobj unbox accessors
  (`il2cpp_list_obj_to_array_dt/_ts/_guid/_dtoffset`), so the snapshot holds
  the family's own cell instead of a boxed pointer;
- `ValueElemKindFor` keeps `kElemKindDateTime` / `kElemKindTimeSpan` on a
  materialized result array;
- four new `il2cpp_linq_list_default_if_empty_val_<fam>` entries write the
  caller's default into the fresh one-element cell (DateTime through
  `il2cpp_datetime_to_array_bits`, never a raw struct copy).

`MethodMap.Linq.cs`: `LinqListKindOfElemByte` resolves the four names, the
`DefaultIfEmpty(source, value)` arm binds the new entries, and the tracked
result-array name now comes from the NAME-aware `LinqArrayTypeNameExact`
overload (a DateTime list's snapshot is a `System.DateTime[]`, not an
`object[]`).

The OTHER operators over these list kinds stay a self-describing transpile-time
LOUD: `LinqListAbiSuffix` answers "i4" for an unknown kind, so an unguarded arm
would read a 4-byte slice out of an 8/16-byte cell. Wired today: Count, Any,
LongCount, ToArray, DefaultIfEmpty. The element-returning / predicate / set /
aggregate surfaces need a struct-typed delegate ABI per operator.

### 21.3 e2e

- `audit-fix3-d2j` GREEN: oracle sha == runtime sha = `40126b94`, 351 bytes.
- The temporary LOUD lock case (`loud-linq-die-listvt`) was retired: the shape
  it locked now works, so it became `audit-fix3-d2j` and the
  scripts/sweep-exclude.txt entry was removed.
- Regressions byte-identical: probe-aggrs `fdac4fff`, probe-linqagg2
  `eb49da63`, probe-arrayagg `875acf6e`, probe-floatelem `9245caa6`,
  probe-aggvt2 `12a416bb`, zmatrix `24ba1e99`, audit-fix3-d2a `5aa068ff`,
  -d2b `16508381`, -d2c `aefc3f5b`, -d2d `935b78eb`, -d2e `eb7bf87b`,
  -d2g `855a96c3`, -d2h `c3b071ff`, -d2i `f5d029ee`.
- Unit 3964/3964 (baseline 3946 + 18 new resolver assertions). C0: il2cppc
  Debug build 0 errors.

### 21.4 Still open after this seat

- `List<DateTime>` / `List<TimeSpan>` / `List<Guid>` / `List<DateTimeOffset>`
  with the element-returning (First/Last/ElementAt/Single/Contains), predicate
  (Where/All/TakeWhile/SkipWhile/Count(pred)/Any(pred)), set
  (Take/Skip/Reverse/Concat/Distinct/Except/Union/Intersect) and Aggregate
  operators -- each needs a struct-typed delegate ABI (the array surface has
  them: `il2cpp_linq_where_dt`, `il2cpp_linq_first_dt`, ...). The list entries
  are thin snapshot wrappers over those kernels, so the remaining work is
  mechanical but broad; it stays a transpile-time LOUD until then.

## 22. impl-loudvalstruct (D follow-up): the SOURCE-ONLY aggregates over a List<decimal>

### 22.1 The defect (measured, reported by the impl-listdecalloc seat)

`List<decimal>.Sum()` (and Min / Max / Average) produced uncompilable code:

```
error: no match for 'operator=' (operand types are
       'il2cpp::runtime::Il2CppDecimal' and 'int32_t')
```

RED evidence: `audit-fix3-d2k`, hard cmake failure against a 340-byte net48
oracle.

Two independent causes, both in the element-family resolution:

1. `TryMapLinqList`'s source-only suffix table covered only "2"/"3"/"4", so
   every other list kind -- including the decimal kind 12 -- fell to the int32
   entry, and the runtime has no decimal source-only kernel at all
   (`il2cpp_linq_list_sum_dec` did not exist).
2. `LinqListKind` only recognised the AOT underscore suffix
   ("List`1_System.Decimal"). A METHOD PARAMETER / FIELD of a generic List is
   tracked in the ANGLE form ("List`1<V:System.Decimal>"); its suffix
   "<V:System.Decimal>" is longer than 1, so it matched the "nested generic ->
   reference" arm. Because a 0x11 element also folds into "ref" in
   `LinqSourceFamily`, the reference tag then survived even the name-aware
   check.

### 22.2 Fixed

- `linq_list.cpp` / `linq_list.h`: four SOURCE-ONLY decimal entries --
  `il2cpp_linq_list_sum_dec` / `_min_dec` / `_max_dec` / `_average_dec` (T3
  result slot, decimal arithmetic through `icall_decimal_*`, empty source
  throws for Min/Max/Average like net48).
- `MethodMap.Linq.cs`: the source-only aggregate arm binds them for kind 12.
- `LinqListKind`: the 0x11 branch resolves BOTH tracked name forms (underscore
  and angle) through the new `LinqListKindOfElemName`, before the `srcFam ==
  "ref"` fallback; the angle form also has its own arm in the suffix switch.
  The same helper keeps a `List<DateTime>`-style parameter from folding into
  the reference tag for the other operators.

### 22.3 e2e

- `audit-fix3-d2k` GREEN: oracle sha == runtime sha = `7ada338a`, 16 records
  (3 non-empty lists across local/ToList shapes, empty-list Sum plus the three
  throwing forms, values beyond int32 range, negatives).
- `audit-fix3-d2j` (value-struct list DefaultIfEmpty) still GREEN `40126b94`.
- All 6 regression probes plus the 9 audit-fix3 cases byte-identical.
- Unit 3969/3969 (baseline 3946 + 23 new assertions). C0: il2cppc Debug build
  0 errors.

## 23. impl-groupbykey: the GroupBy KEY-FAMILY matrix (b5-gaps 17.2 (3))

### 23.1 The defect (measured)

Two transpile-time LOUDs and three SILENT WRONG VALUES.

LOUD (transpile log at 856edc87):

```
Enumerable.GroupBy<int32,System.TimeSpan> not wired: ... a value-type
  (System.TimeSpan) key has no runtime key carrier (see docs/audit/b5-gaps.md)
Enumerable.GroupBy<int64,System.DateTime> not wired: ... the stage-1 runtime
  LINQ surface is int32 (0x08) element-family for this operator
Enumerable.GroupBy<int32,Cases.Color> ... a value-type (Cases.Color) key has
  no runtime key carrier
```

SILENT WRONG VALUES (the highest-priority class; each produced a WRONG BUCKET
COUNT with no diagnostic at all):

1. **A decimal key compared by its RAW 16-byte image.** net48 compares
   `decimal` by VALUE, so `1.0m / 1.00m / 1.000m` is ONE bucket. The old
   `GroupKeysEqual` did `memcmp` over the raw carrier, and the three literals
   differ in their SCALE bits -> three buckets.
2. **A DateTime key compared `ticks + kind`.** net48 compares `ticks` only
   (`DateTime.Equals` ignores `Kind`), so the same instant as Utc / Local /
   Unspecified is ONE bucket; the old comparer used `keyHi = kind` -> three.
3. **A reference key reached the result selector as the int32 carrier.** In
   `ApplyResultSelector`, every `keyKind >= 8` that was not 8/9 fell into the
   Guid arm, and `keyKind 11` (reference) never had an arm at all -- the leaf's
   first parameter (declared `Il2CppObject*`) received the int32 key slot as a
   pointer. Measured blast radius while bisecting: the `probe-groupbykey`
   `[refrs]` records emitted ~58 MB of garbage instead of two short strings.

Two further traps found while fixing (the first was mine, the second
pre-existing):

- a duplicated `if (ed != nullptr)` guard nested the whole element-family
  chain, so every key-only bucket stored a ZERO element (`p0grp` `[int] e0=0`
  against an oracle `e0=5`). Caught by the `p0grp` regression, not by the new
  probe;
- `il2cpp_linq_group_by_full_key`'s key-only `elemKind` derivation covered only
  the Guid (6) and DateTime (5) sources, so a key-only DECIMAL source defaulted
  to the int32 bucket and stored zeros. The derivation now covers decimal (4),
  int64 (2), double (3) and narrow (0) as well.

Correction to an earlier internal note (kept for the record): the old
`GroupKeysEqual` decimal arm's `keyHi == existing->keyHi` was NOT a
self-comparison -- it compares the caller's carrier against the stored one. The
real defect there is the comparison RULE (raw image instead of value), which is
what item 1 above describes.


### 23.2 Root cause

The key carrier was a single 16-byte `(keyLo, keyHi)` pair with a per-family
`switch` copy-pasted into FIVE source-family entry points (`int32`, reference,
decimal, DateTime, Guid), each with its own hand-written selector ABI
(`InvokeSelDecI4`, `InvokeSelDecDtKey`, ...). Consequences:

- a new key family needed a new hand-written ABI per source family (so
  TimeSpan / DateTimeOffset / user-enum simply did not exist);
- a new SOURCE family needed a new hand-written key dispatch (so `long[]`,
  `double[]` and `byte[]` sources could not GroupBy at all);
- the comparison rules lived in one place but were written per family, so the
  decimal (value) and DateTime (ticks-only) rules were never expressed.

### 23.3 Fixed

`src/runtime/src/linq_map.cpp` / `linq_map.h`:

- `InvokeGroupKeySel<Src,Key>` / `InvokeGroupElemSel<Src,Elem>` -- ONE generic
  invocation that reproduces every leaf ABI exactly (int32 slot, int64/double
  registers, `Il2CppObject*`, and the hidden-pointer aggregate for the 8/16-byte
  value structs), so any (source, key) pair is served without a new ABI.
- `InvokeGroupEnumKeySel<Src>` + `Il2CppEnumKeyImage` -- a user enum is emitted
  by TypeEmitter as `struct E { Il2CppObject _obj; int32_t value__; }`
  (instanceSize 16, value__ at offset 8), so the runtime reads the payload
  through that image without naming the enum's generated C++ type.
- `ComputeGroupKey<Src>` / `ComputeGroupElem<Src>` -- the single dispatch both
  kernels now use. Key kinds: `0..6` int32-slot (narrowed), `7` int64/uint64,
  `8` Decimal, `9` DateTime, `10` Guid, `11` reference, `12` TimeSpan,
  `13` DateTimeOffset, `14` user enum. All 15 are enumerated; there is no
  `default` arm that silently reinterprets a family.
- `GroupKeysEqual` rewritten to the net48 rules: 8 -> `icall_decimal_equals`
  (VALUE), 9 -> `ticks` only, 10 -> raw 16 bytes, 12 -> `ticks`, 13 ->
  `utcTicks` only (offset excluded), 14 -> int32 `value__`, 11 -> reference
  equality, 7 -> `ComparerKeyEqualsLong`, 0..6 -> `ComparerKeyEquals`.
- `ApplyResultSelector` gained the missing 11 / 12 / 13 / 14 arms, so the leaf
  receives the REAL key (pointer / `Il2CppTimeSpan` / `Il2CppDateTimeOffset` /
  enum image) instead of the int32 carrier.
- `NarrowCellAt` + `il2cpp_linq_group_by_full_key_cell` -- srcKind 7 (the
  1/2/4-byte integral families) reads its cell at the real width before
  widening into the int32 selector slot; srcKind 5/6 read int64/double cells.
- key-only `elemKind` derivation now covers decimal (4) / int64 (2) /
  double (3) / narrow (0) sources as well; `il2cpp_linq_group_by_dec_key` calls
  the shared `ComputeGroupKey`, so a decimal source accepts every key family.
- new accessors `il2cpp_linq_grouping_get_key_ts` / `_dto` (a user-enum key
  keeps the plain int32 `get_key`, its only payload being `value__`).

`src/il2cppc/Emit/MethodMap.Linq.cs`:

- `KeyKindForNamed`: +12 `System.TimeSpan`, +13 `System.DateTimeOffset`, +14 a
  user enum (resolved through `TypeEmitter.SessionTypes` +
  `BaseTypeName == "System.Enum"`), and a whitelisted BCL enum -> keyKind 0
  (its translated value is a plain `int32_t`). A user STRUCT still returns -1.
- `LinqGroupingKeyMarker`: +`_key_ts` / `_key_dto`; the get_Key dispatch binds
  the two new accessors.
- `TryMapLinqWide` gained a `GroupBy` arm for the int64 (srcKind 5) / double
  (6) / narrow (7, via the `_cell` entry + the source cell code) source
  families. A `float[]` source returns null and keeps its LOUD.

### 23.4 e2e

`tests/cases/probe-groupbykey` (new, 11 records, oracle-vs-runtime sha
equality):

- `[ts]` TimeSpan key over int32 -> 3 buckets by ticks; `[ts2]` a 1.5 s span
  built from `FromMilliseconds` and from `FromTicks` is ONE bucket;
- `[enum]` user-enum key -> 1/2/3 by `value__`;
- `[i8]` long source (key-only buckets hold the source values) and `[i8dt]`
  long source with a DateTime key;
- `[narrow]` byte source; `[d]` double source;
- `[dto]` DateTimeOffset key -> the same instant at +00:00/+02:00 is ONE
  bucket and get_Key reports the FIRST key's offset;
- `[refrs]` reference key + result selector -> the real key object;
- `[deckey]` `1.0m / 1.00m / 1.000m` -> ONE bucket (2 with `2m`);
- `[dtkey]` Utc/Local/Unspecified with the same ticks -> ONE bucket.

RED at baseline (transpile LOUD, 0 records): `GroupBy<int32,System.TimeSpan>`.
GREEN: oracle sha == runtime sha =
`73a88e15accb68b86bb6b8786252350690ec5c9e423014b5e3dc5bde5be55535`,
544 B, 11/11 records, diff 0.

Regressions unchanged: `p0grp` `0b12732d`, `linqcase` `2d76fda3`,
`linqorder2` `6a72ece6`, `probe-aggrs` `fdac4fff`, `probe-linqagg2` `eb49da63`,
`loud-linq-decsetops` `454c4e2e`, `zmatrix` `24ba1e99` -- all ok.

Unit: 3993/3993 (baseline 3974 + 19 new assertions in
`tests/unit/MethodMapTests.GroupByKey.cs`). C0: il2cppc Debug rebuild
0 errors, and the only warnings are the three pre-existing `CS8601` in
`Ir/StackFold.cs` (verified byte-identical at HEAD) -- zero new warnings.

### 23.5 Still open after this seat (registered)

- **A `float[]` (System.Single) GroupBy source** stays LOUD. It is NOT a
  key-carrier problem -- the key dispatch already accepts every family -- it is
  the BUCKET ELEMENT surface that is missing:
  (a) there is no `GroupElemKind::F`, so `NewGroupElementArray` cannot
  materialize a `kElemKindF` bucket (the `I8`/`D` buckets box through
  `icall_box_long`/`icall_box_double`; a float bucket would need
  `icall_box_float`, which exists but stores a widened double, so
  `get_elements`/`ToArray` would have to keep the 4-byte cell while the boxed
  form widens -- a type change that needs its own design);
  (b) there is no `il2cpp_ienum_get_current_f` carrier (`MethodRefResolver`
  wires `_ref/_dec/_dt/_guid/_i8/_d/_i4/_obj` only), so a
  `foreach (float f in bucket)` has no result slot.
  Both are runtime-surface additions outside the GroupBy KEY scope; the LOUD
  keeps naming the real reason ("int32 (0x08) element-family"), never a wrong
  bucket. Registered as the follow-up for a `float` element-family seat.
- **A user STRUCT key** (`GroupBy<int, SomeStruct>`) stays LOUD by design: the
  generated struct has no `Equals`/`GetHashCode` vtable entry the runtime can
  call for bucketing, so there is no by-value key carrier ABI to bind. The
  message names the key type ("a value-type (Cases.Point) key has no runtime
  key carrier"), so the gap is self-describing.
- **A user `IEqualityComparer<TKey>` over the new key kinds** (12/13/14) is
  LOUD: the comparer would have to be dispatched per family (`GroupKeysEqual`
  refuses it explicitly). The default (null) comparer covers every new kind.

