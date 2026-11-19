# System.Text.Encoding's metadata surface

Status: GREEN (`probe-encodingprops`, 85 lines, byte-exact against the net48 oracle).

## What was missing

The four Encoding singletons already worked: `Encoding.UTF8`, `Encoding.Unicode`,
`Encoding.BigEndianUnicode`, `Encoding.ASCII` returned real objects and
`GetBytes`/`GetString`/`GetChars` round-tripped. What had no mapping arm at all
was the **metadata** surface:

```
get_CodePage  get_WindowsCodePage  get_WebName  get_EncodingName
get_HeaderName  get_BodyName  get_IsSingleByte  get_IsReadOnly
get_IsBrowserDisplay  get_IsBrowserSave  get_IsMailNewsDisplay
get_IsMailNewsSave  IsAlwaysNormalized  Clone  Equals  GetHashCode
GetMaxByteCount  GetMaxCharCount  GetEncoding(int)  GetEncoding(string)
get_Default  get_UTF32  get_UTF7
```

Every one failed with the same whole-method loss:

```
il2cppc error: Program::Main: System_Text_Encoding_get_Default_f4fdfc6ec7188bb3:
  not wired in M5 (BCL owner has no mapping arm and is not defined in the input
  assembly -- a phantom generated-name call would never link)
```

## The object model

An encoding is `{ Il2CppObject obj; int32_t page; int32_t emitBom; }`. So the
code-page getters are direct reads, and the name getters are a page -> string
table. Every value below is **pinned by the oracle**, not derived:

| page | webName | encodingName | header/body | windowsCodePage | singleByte | browser disp/save | mailnews disp/save |
|---|---|---|---|---|---|---|---|
| 65001 | utf-8 | Unicode (UTF-8) | utf-8 | 1200 | false | true/true | true/true |
| 1200 | utf-16 | Unicode | utf-16 | 1200 | false | false/true | false/false |
| 1201 | utf-16BE | Unicode (Big-Endian) | utf-16BE | 1200 | false | false/false | false/false |
| 20127 | us-ascii | US-ASCII | us-ascii | 1252 | true | false/false | true/true |

`IsAlwaysNormalized` answers **false for all four** (the first implementation
returned `isSingleByte`, which made ASCII answer true -- the single diverging
line in the first diff).

## Encoding.Default must track the host

net48's `Encoding.Default` is the machine's ANSI code page, so a hardcoded
constant would be wrong on any machine whose locale differs. The runtime reads
`GetACP()` -- the same source net48 reads -- so the two agree everywhere:

```cpp
int32_t acp = 936;              // fallback for non-Windows
#if defined(_WIN32)
    acp = static_cast<int32_t>(::GetACP());
#endif
```

On a zh-CN host that is 936 / gb2312; on an en-US host both sides answer 1252.
A `936` row exists in the name table so `Default.WebName` is answered from the
same table as every other page.

## The inheritance chain was wrong (a castclass failure)

The probe's first runtime run crashed with:

```
Unhandled managed exception: Unable to cast object to the target type.
```

The generated code was `(Encoding)utf8Instance`, i.e.
`il2cpp_is_instance(t0, &System_Text_Encoding_class)`. The conformance test walks
the base chain, and **all four** Encoding descriptors rooted at
`System_Object_class`, so `UTF8Encoding` was unrelated to `Encoding`:

```cpp
// before: every class rooted at Object
ENCODING_CLASS(System_Text_UTF8Encoding_class, "System.Text.UTF8Encoding");
// macro hardcodes &System_Object_class
```

The macro now takes the base, and the three concrete encodings inherit from
`Encoding`:

```cpp
ENCODING_CLASS(System_Text_Encoding_class,      "System.Text.Encoding",     &System_Object_class);
ENCODING_CLASS(System_Text_UTF8Encoding_class,  "System.Text.UTF8Encoding", &System_Text_Encoding_class);
ENCODING_CLASS(System_Text_UnicodeEncoding_class,"System.Text.UnicodeEncoding", &System_Text_Encoding_class);
ENCODING_CLASS(System_Text_ASCIIEncoding_class, "System.Text.ASCIIEncoding", &System_Text_Encoding_class);
```

This is a general rule: a BCL subtype descriptor must name its real base, or
every `(BaseType)subtypeInstance` cast fails.

## Capacity contract

```
GetMaxByteCount(n)  UTF-8: 3n + 3   (3 bytes per UTF-16 unit + 3-byte BOM)  -> 10 = 33
                    UTF-16: 2n + 2  ASCII: n
GetMaxCharCount(n)  n / bytesPerUnit + 1 (one char for the BOM)             -> 10 = 11
```

`emitBom` participates: the singleton instances are created with `emitBom=1` for
UTF-8/UTF-16 and `0` for ASCII, which is why the ASCII count has no BOM term.

## Clone / Equals / GetHashCode

* `Clone()` returns a **new** object with the same page and emitBom (net48's
  memberwise copy). The concrete descriptor follows the page.
* `Equals(object)` compares page **and** emitBom, not the reference. The probe
  pins the shape: a UTF-8 instance equals the UTF-8 singleton and differs from
  the other pages.
* `GetHashCode` is a stable function of the same state (the probe asserts only
  non-zero), so it cannot vary between runs the way a pointer-identity answer
  would.

## Evidence

```
[BASH] net48 oracle, 85 lines; runtime first pass 84/85 (only alwaysnorm)
[BASH] difftest -Case probe-encodingprops:
       case=probe-encodingprops compiler=mingw ok=True
       oracle  code=0 out=1383B sha=d28e3dcfdab25b14e28dd70ef5c7e4c55722536599523695626030d1f0a8ed96
       runtime code=0 out=1383B sha=d28e3dcfdab25b14e28dd70ef5c7e4c55722536599523695626030d1f0a8ed96
       firstDiff stdout=-1 stderr=-1 records(oracle=0 runtime=0) diff=0
[BASH] mutation -- windowsCodePage + 1: ok=False, firstDiff stdout=81 (RED)
[BASH] restore -> ok=True, sha unchanged (GREEN)
[BASH] g++ -std=c++17 -c: encoding/char/stringext/array/interlocked/
       readonlycollection -> 0 errors, 0 warnings
[BASH] dotnet build src/il2cppc/il2cppc.csproj -c Release -> 0 warnings, 0 errors
[BASH] publicapi_unmapped_gate (net48 mscorlib input):
       unmapped baseline=445 current=445 closed=0 new=0
       resolved baseline=1526 current=1526 regressed=0 gained=0
       (was 468 / 1503 -- 23 Encoding rows moved across)
[BASH] regression: encodingcase / encodingconvcase / p-nb9-fix-enc /
       p-nb4-gc / probe-charstr all ok=True
```

## Scope

Models the four conversion pages plus the host ANSI page. `GetEncoding` throws
`ArgumentException` for an unsupported code page or name, matching net48.
`Convert`, `GetEncodings`, `RegisterProvider`, `GetDecoder`/`GetEncoder` and the
fallback setters remain unmapped. `UTF32`/`UTF7` are created as plain encoding
objects with their .NET page numbers (12000/65000); the conversion table does not
model them, so only their metadata surface is verified.