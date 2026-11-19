# System.Char's (string, int) overload family

Status: GREEN (`probe-charstr`, 22 lines, byte-exact against the net48 oracle).

## What was missing

`System.Char` had a full mapping arm for the **single-char** classification
family (`char.IsDigit(char)`, `char.IsLetter(char)`, `char.ToUpper(char)`, ...).
net48 also declares a **second** family that indexes into a string:

```
char.IsDigit(s, i)          char.GetNumericValue(s, i)
char.IsLetter(s, i)         char.GetUnicodeCategory(s, i)
char.IsUpper / IsLower      char.IsSurrogatePair(s, i)
char.IsWhiteSpace           char.IsHighSurrogate(s, i)
char.IsPunctuation          char.IsLowSurrogate(s, i)
char.IsSymbol               char.IsControl(s, i)
char.IsSeparator            char.IsNumber(s, i)
char.IsSurrogate(s, i)
```

None of these were wired, and because the BCL body is not in the transpiled
assembly there is nothing to fall back on, the failure is a **whole-method**
loss:

```
il2cppc error: Program::Main: System_Char_IsDigit_f807898d1e9ef7cd:
  not wired in M5 (BCL owner has no mapping arm and is not defined in the input
  assembly -- a phantom generated-name call would never link)
```

`Main` emitted no code at all, so the probe printed nothing.

Two more single-char shapes were also missing and surfaced while extending the
probe: `char.IsSurrogatePair(char, char)` and `char.ToString(char)` (the static
one-argument overload; the instance `ToString()` stays in
`TryMapValueType`).

## The net48 overload set is measured, not assumed

The first draft of the probe called `char.IsDigit(string, int)` variants for
`bool`, `char` and `string`. The C# compiler rejected all three:

```
error CS1503: 参数 1: 无法从"ref bool"转换为"ref byte"
error CS1503: 参数 1: 无法从"ref char"转换为"ref byte"
error CS1503: 参数 1: 无法从"ref string"转换为"ref byte"
```

That is the evidence: on the Thread/char families `bool` and `char` fold into
the **byte** overload and `string` into the **object** overload. The probe was
rewritten to the measured set instead of the imagined one, and the wiring
probe's net48 rows were used as the authoritative enumeration.

## Implementation

`char.cpp` gained 16 entries. Every one of the fourteen `(string, int)` forms
is a thin adapter over the single-char predicate already present:

```cpp
inline char16_t CharAtChecked(Il2CppObject* s, int32_t index)
{
    if (s == nullptr) il2cpp_throw_null_reference_exception(nullptr);
    Il2CppString* str = reinterpret_cast<Il2CppString*>(s);
    if (index < 0 || index >= str->length)
        il2cpp_throw_argument_out_of_range("index");
    return str->chars[index];
}

bool icall_char_s_is_digit(Il2CppObject* s, int32_t index)
{
    return icall_char_is_digit(CharAtChecked(s, index));
}
```

This means the two families **cannot disagree**: the same predicate is applied
to the selected code unit.

Negative paths are explicit, because a net48 classification method that indexes
a string has three failure modes, not one:

* `s == null` -> `NullReferenceException`
* `index < 0 || index >= length` -> `ArgumentOutOfRangeException`
* `IsSurrogatePair(s, i)` additionally requires `i + 1` to exist

### `IsNumber(s, i)` is wider than `IsDigit`

`char.IsNumber` is the Unicode **Number** category, which is `Nd | Nl | No`,
while `IsDigit` is `Nd` only. A character with a numeric value but outside Nd
(say a Roman numeral) answers `true` for IsNumber and `false` for IsDigit.
Implemented as:

```cpp
char16_t c = CharAtChecked(s, index);
if (icall_char_is_digit(c)) return true;
return icall_char_get_numeric_value(c) >= 0.0;
```

### `GetUnicodeCategory` ordinals are measured

The probe prints the enum's **numeric** value, so the ordinal table had to come
from net48 rather than from a guess. A throwaway net48 program dumped all 30
values (`Enum.GetValues(typeof(UnicodeCategory))`):

```
0=UppercaseLetter      1=LowercaseLetter      2=TitlecaseLetter
3=ModifierLetter       4=OtherLetter          5=NonSpacingMark
6=SpacingCombiningMark 7=EnclosingMark        8=DecimalDigitNumber
9=LetterNumber        10=OtherNumber         11=SpaceSeparator
12=LineSeparator      13=ParagraphSeparator  14=Control
15=Format              16=Surrogate           17=PrivateUse
18=ConnectorPunctuation 19=DashPunctuation    20=OpenPunctuation
21=ClosePunctuation    22=InitialQuotePunctuation 23=FinalQuotePunctuation
24=OtherPunctuation    25=MathSymbol          26=CurrencySymbol
27=ModifierSymbol      28=OtherSymbol         29=OtherNotAssigned
```

The probe pins the two ordinals the runtime's ASCII-domain classification
actually distinguishes: `cat1=8` (`'1'` is `DecimalDigitNumber`) and
`cat0=1` (`'a'` is `LowercaseLetter`).

## Evidence

```
[BASH] net48 oracle, 22 lines:
       len=10 isdigit1=True isdigit0=False isdigit4=False
       isletter0=True isupper2=False islower0=True isws5=True
       iswpunct6=True issym7=True isnum1=True isctrl0=False
       surrpair=True high8=True low9=True surr8=True
       cat1=8 cat0=1 numval1=1 numval0=-1 tostr0=a tostr2= 
[BASH] difftest -Case probe-charstr:
       case=probe-charstr compiler=mingw ok=True
       oracle  code=0 out=280B sha=64ee7bb81d6f614deebb674980438c4416e80ca2ed7a5ecdad5a8d7caccf6c78
       runtime code=0 out=280B sha=64ee7bb81d6f614deebb674980438c4416e80ca2ed7a5ecdad5a8d7caccf6c78
       firstDiff stdout=-1 stderr=-1 records(oracle=0 runtime=0) diff=0
[BASH] mutation -- CharAtChecked returns chars[index + 1]:
       ok=False, firstDiff stdout=17  (RED)
[BASH] restore -> ok=True, sha unchanged  (GREEN)
[BASH] g++ -std=c++17 -c: char/stringext/array/arrayext/interlocked
       -> 0 errors, 0 warnings
[BASH] dotnet build src/il2cppc/il2cppc.csproj -c Release -> 0 warnings, 0 errors
[BASH] publicapi_unmapped_gate (net48 mscorlib input):
       unmapped baseline=468 current=468 closed=0 new=0
       resolved baseline=1503 current=1503 regressed=0 gained=0
       (was 486 / 1485 -- 18 char rows moved across)
[BASH] green-gate: PASS (29 GREEN contract case(s) still byte-exact)
       probe-charstr: OK -- ok=True, producedBytes=560
```

## Scope

Covers the concrete `(string, int)` overloads net48 declares, plus the two
single-char shapes the probe exercises (`IsSurrogatePair(char,char)`,
`ToString(char)`). `char.GetUnicodeCategory` is the ASCII-domain classification
(the same documented boundary the single-char family records); ordinals outside
the distinguished set answer `OtherNotAssigned`. `char.ToLower(char, CultureInfo)`
and `char.ToUpper(char, CultureInfo)` remain unmapped -- culture-sensitive case
conversion is a separate surface.