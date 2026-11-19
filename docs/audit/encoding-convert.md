# System.Text.Encoding.Convert and IsAlwaysNormalized(form)

Status: GREEN (`probe-encprop`, byte-exact against the net48 oracle, sha
`74681a6d1f4d882759c9157e9f62e7655f40f5427196236191e83c5bc73caf3c`, 258B).

## What was missing

Two members still had no mapping arm:

```
Convert(Encoding, Encoding, byte[])
Convert(Encoding, Encoding, byte[], int, int)
IsAlwaysNormalized(NormalizationForm)
```

`IsAlwaysNormalized(NormalizationForm)` was the sharper find: the runtime
icall already existed (`icall_encoding_get_is_always_normalized_form`), but
the map arm only accepted a signature code of `0x08` (int32).  The parameter
is a VALUETYPE enum, which decodes as `0x11`, so the arm never fired and the
whole method fell to the generated-name path.

## Net48 measurements

| Behaviour | net48 |
|---|---|
| `Convert(unicode, utf8, "abc" bytes)` | 3 bytes `61-62-63` |
| `Convert(unicode, utf8, src, 2, 2)` | 1 byte `62` |
| `Convert(ascii, utf8, {65,66})` | `41-42` |
| `Convert(utf8, unicode, {65,66,67})` | 6 bytes (3 UTF-16LE units) |
| `Convert(null, ...)` | ArgumentNullException |
| `Convert(..., index, count)` overrun / negative | ArgumentOutOfRangeException |
| `IsAlwaysNormalized(form)` for UTF-8 and ASCII, all four forms | false |

`Convert` is defined as `dst.GetBytes(src.GetString(bytes))`; the ranged form
slices the byte array first, so the decode never reads past `count`.

## Implementation shape

* `icall_encoding_convert(src, dst, bytes)` reuses the file-local
  `GetStringForPage` / `GetBytesForPage` dispatch, so it inherits every
  per-page codec already pinned by `probe-encodingprops`.
* `icall_encoding_convert_range` copies the slice into a fresh array before
  decoding (the whole-array decode would read past `count`).
* The map arm accepts `0x11` (VALUETYPE) alongside `0x08` for the
  `NormalizationForm` parameter.

## Mutation evidence

Swapping the decode source (`GetStringForPage(src, ...)` -> `GetStringForPage(dst, ...)`)
turns the probe RED at stdout record 154; restoring it returns the sha above.

## Still unmapped

`GetDecoder`, `GetEncoder`, `GetEncoding(3-arg)`, `GetEncodings`,
`RegisterProvider`, and the four `DecoderFallback`/`EncoderFallback`
accessors remain unmapped: the runtime has no `Decoder` / `Encoder` / `*Fallback`
class descriptors and the transpiler has no owner for them.  Those need the
type family introduced before the members can be wired.
