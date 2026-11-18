# Array lower bounds: modelled in the header, applied exactly once

`Array.CreateInstance(Type, int[], int[])` silently dropped its third argument.
The mapping arm passed two arguments for a three-argument call:

```
if (count == 3 && ps[1] == 0x1D && ps[2] == 0x1D)
    return I("il2cpp::il2cpp_array_create_instance_dims", 2, ...,
        new[] { "", "a" }, ...);   // lowerBounds never reaches the runtime
```

So `GetLowerBound` always answered 0, and an array created with
`lowerBounds {5}` behaved as if it were zero-based.

## Three parts

**1. The header carries the bounds.** `Il2CppArray` gained
`int32_t lowerBounds[IL2CPP_MAX_ARRAY_RANK]`, one entry per dimension, next to
`dims`. Every allocator zeroes it, so an ordinary array is byte-identical to
before; only `CreateInstance` with explicit bounds writes a non-zero value.

**2. The runtime models it.** `il2cpp_array_create_instance_dims_lb` takes the
third argument, validates that its length matches `lengths`, and stamps the
bounds after allocation. `GetLowerBound` returns `lowerBounds[dim]` and
`GetUpperBound` returns `lowerBounds[dim] + dims[dim] - 1` (net48: -1 for an
empty array).

**3. The index arithmetic subtracts the bound exactly once.** This is where the
first attempt was wrong, and the failure is worth recording:

- `RequireIndex` (1-D) and `RequireDimIndexExtra` (per dimension) now convert an
  index in the array's own coordinate system to a zero-based offset.
- `FlatIndex2`/`FlatIndex3` already call `RequireDimIndexExtra`, so they return
  an ALREADY-CONVERTED flat offset. The multidim entry points used to pass that
  offset into the 1-D `il2cpp_array_set_value`, which subtracted
  `lowerBounds[0]` a second time. Measured: `lb`/`idx`/`zero`/`md` all passed
  while `mdidx` threw `IndexOutOfRangeException`.

The fix splits the 1-D body into an offset-taking core
(`il2cpp_array_get_value_at_offset` / `il2cpp_array_set_value_at_offset`) and
keeps the 1-D entry points as thin converters. The multidim entry points call
the core directly, so the bound is applied once on every path.

## Evidence

`tests/cases/probe-arraylower` (8 lines), byte-identical to the net8 oracle
(`sha=8fd74a0fcd8fac5c4f39c3df16a558889f124599d4e5f2cdafc93019b5c0b54f`):

```
lb:5:7:3
idx:11:22
zero:0:3
md:1:10:2:12:6
mdidx:99:77
lowbad:ok
neg:-2:0
negidx:5
```

`lowbad:ok` is the negative case: an index below the lower bound must throw
`IndexOutOfRangeException`. `neg`/`negidx` cover a negative lower bound, which
net48 allows.

## Mutation evidence

| mutation | result |
|---|---|
| 1-D conversion ignores `lowerBounds[0]` | `idx` throws `IndexOutOfRangeException`, exit 1 |
| per-dimension conversion ignores `lowerBounds[dim]` | `mdidx` throws, exit 1 |
| restored | 8/8 lines match the oracle, exit 0 |

Both mutations are load-bearing on separate paths (1-D and multidim), which is
what the double-subtraction defect required: fixing one path alone left the
other broken.

## Regression

18 cases pass, including every array case in the tree (`probe-arraybase`,
`probe-arraycast`, `probe-arrayclone`, `probe-arraycreate`, `probe-arraymd`,
`arrgen2`, `arraydeep2`, `arrdeep`, `multidim-vt`, `p-gn9mdim`, `p-gn10mdim`,
`p-gn11mdim0x15`, `p-gn11mdimalloc`, `p-multidimarg`, `pinvoke-multidim`).

## Still not covered

`Array.CreateInstance` with a non-zero lower bound is now modelled for the
`(Type, int[], int[])` overload. The `(Type, int, int)` / `(Type, int, int, int)`
overloads have no lower-bound parameter in net48, so they are unaffected.
