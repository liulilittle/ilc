# GAPS.md -- listops3 recorded findings (e2e batch C78, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 35-line
byte-exact). Active probes drive the List<int32> COMBINATION dimension on the
WIRED surface: List<int> -> ToArray() independent int[] snapshot -> Array.Sort
/ Array.Reverse / Array.FindAll / Find / Exists / TrueForAll with the
single-cast static-predicate ABI (arrfuncs precedent), plus ToArray snapshot
independence, Sort+Reverse involutive composition, FindAll->Sort and
Reverse->FindAll composition, a scripted Insert/Remove/RemoveAt/set_Item build
fed to a sort, and capacity-ctor growth feeding a sort. Gaps below are
RECORDED, NOT FIXED (this batch creates test cases only).

## D-LO3-1 -- List<T>.Sort/Reverse/FindAll instance methods are NOT wired

- **Phenomenon**: `List<int>.Sort()` (and by extension `Reverse()`,
  `FindAll()`) has no arm in TryMapList (MethodMap.List.cs ends at ToArray,
  D-LISTQ-1..5) and no runtime icall on the il2cpp_list_i4 surface. A call
  resolves to the generated-name path -> undefined C++ symbol -> cmake build
  failure (probe verified 2026-08-13: `List<int>.Sort()` transpiled to
  `System_Collections_Generic_List_1_int_Sort_66fad52831b59cda(t0)`).
- **Workaround exercised**: the net48-equivalent composition goes through the
  wired bridge `List<int>.ToArray() -> Array.Sort / Array.Reverse /
  Array.FindAll`, which IS byte-exact. The list domain's sort/reverse/findall
  surface is the acceptance gap.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.List.cs` (no Sort/
  Reverse/FindAll arms) + `src/runtime/src/listi4.cpp` (no sort/reverse/find
  icalls on the int32 list surface).

## D-LO3-2 -- Array.Clone is not wired (Array member surface)

- **Phenomenon**: `(int[])ca.Clone()` (System.Array::Clone) has no BCL
  mapping -> generated-name path -> undefined C++ symbol -> build fail. The
  probe was replaced with a literal reversed array. The Array member surface
  (Clone/GetLength/etc.) is outside the wired icall set.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Array.cs` (no Clone
  arm).
