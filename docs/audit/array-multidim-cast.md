# Multidim array casts: the rank is part of the array class identity

`(int[,])o` did not fail at run time -- it failed at TRANSPILE time:

```
il2cppc error: Program::Main: TypeSpec signature byte 0x14 not
GenericInst(0x15) in M5
  at CodeGenerator.GenericTypeSpecDefinitionName(...) line 6613
  at CodeGenerator.EmitTask9Call(...) line 11557
```

The castclass/isinst TypeSpec arm only recognised `0x1D` (SZARRAY). A
multi-dimensional array target is `0x14` (ELEMENT_TYPE_ARRAY), so it fell
through to `GenericTypeSpecDefinitionName`, which accepts only `0x15`
(GENERICINST) and threw.

## Why the fix is not just "accept 0x14"

net48 keeps `int[]` and `int[,]` as DISTINCT types. The conformance test is an
identity comparison (`il2cpp_is_instance` walks the base chain comparing
descriptors), so if both ranks resolved to the same descriptor then

- `(int[,])new int[]{...}` would succeed, and
- `(int[])new int[2,3]` would succeed,

both of which net48 rejects with `InvalidCastException`. The rank therefore has
to be part of the descriptor's identity.

## Three changes

**1. The transpiler decodes the 0x14 shape.** `MultidimArrayClassCpp` reads the
element byte with the same ECMA-335 table `SzArrayArrayClassCpp` uses, then the
ARRAY shape (`rank`, `numSizes`, `sizes`, `numLoBounds`, `loBounds`) and emits

```
il2cpp::il2cpp_array_class_for_element_name_rank("<element>", <rank>)
```

**2. The runtime names a multidim class with its rank.** `ArrayClassForElement`
grew a `rank` parameter; a rank > 1 slot is named `"[<element>,<rank>"`
(`"[System.Int32,2"`), and the pool key is `(isRef, rank, element name)`.

The pool lookup had to change with it. It used to compare
`klass.name + 1` against `elemClass->name`, which works only for the unranked
form: for a multidim slot `name + 1` is `"System.Int32,2"`, never equal to
`"System.Int32"`. Every multidim lookup therefore MISSED and allocated a fresh
slot, so the array's class and the cast target were different pointers and the
cast threw. The lookup now builds the full expected name and compares that.

**3. The allocator stamps the rank-carrying class.** `il2cpp_array_new_multi`
takes the `rank > 1` branch and asks for the rank-aware class instead of
reusing `g_int32ArrayClass` / `g_objectArrayClass`. The transpiler's `mdnew_`
arm now passes the element descriptor (`MdSuffixElementClassCpp`) instead of
`nullptr`, which is what lets the allocator name the class at all.

## Evidence

`tests/cases/probe-arraymd` (10 lines), byte-identical to the net8 oracle
(`sha=9969e85b9618e9b2487c36d87cba7539d5eb858ec87d5125fd5f6fcc7411d61f`):

```
md:2:3
as:True
badnull:True
badthrow:ok
elem:42:6
rank1throw:ok
rank2throw:ok
rank1null:True
rank2null:True
md3:8:True
```

The `rank1*` / `rank2*` lines are the load-bearing ones: they assert the
EXCLUSION in both directions (`int[]` must reject `int[,]` and vice versa, on
both the cast and the `as` form). `md3` adds a third rank.

## Mutation evidence

| mutation | result |
|---|---|
| `il2cpp_array_new_multi` forced onto the single-dimension descriptor (`if (false)`) | `(int[,])o` throws `Unable to cast object to the target type`, exit 1 |
| restored | 10/10 lines match the oracle, exit 0 |

A first attempt mutated the class NAME (dropping the rank from the string) and
stayed green. That was a false test, and it identified the real load-bearing
decision: the pool key is the `rank` FIELD, not the name text. The name is only
what `GetType()` reports. The mutation was moved to the allocator branch, which
is the decision that actually separates the two identities.

## Regression

15 cases pass, including every multidim case in the tree (`arrgen2`,
`arraydeep2`, `arrdeep`, `multidim-vt`, `p-gn9mdim`, `p-gn10mdim`,
`p-gn11mdim0x15`, `p-gn11mdimalloc`, `p-multidimarg`, `pinvoke-multidim`) and
the three array-cast cases from the previous rounds.

## Still not covered

`Array.CreateInstance` with a non-zero lower bound is still unmodelled: the
array header has no lower-bound field, so `GetLowerBound` always answers 0.
