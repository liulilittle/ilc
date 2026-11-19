# Array.GetLongLength and the three IList flags

`System.Array`'s generic search surface was already wired
(`Empty`/`Exists`/`Find`/`FindIndex`/`FindLast`/`FindAll`/`TrueForAll`/
`Resize`/`ForEach`), but four members were not, and one of them failed at
TRANSPILE time:

```
warning: method fold failed -> skipped (Program::Main: System.Array::GetLongLength:
System.Array::GetLongLength: not wired in M5 (BCL owner has no mapping arm and is
not defined in the input assembly -- a phantom generated-name call would never
link; receiverType='System.Int32[]' arg0Type='System.Int32[]'))
```

## What was missing

| member | net48 answer | state before |
|---|---|---|
| `GetLongLength(int)` | `GetLength(dim)` as `Int64` | no mapping arm, no runtime |
| `get_IsFixedSize` | `true` (an array cannot grow or shrink) | no mapping arm, no runtime |
| `get_IsReadOnly` | `false` (elements are writable) | no mapping arm, no runtime |
| `get_IsSynchronized` | `false` (no automatic synchronization) | no mapping arm, no runtime |

`get_LongLength` (the property) already existed and is a different member: it
widens the TOTAL length, while `GetLongLength(dim)` widens one dimension.

## The fix

Four runtime functions in `arrayext.cpp` and four mapping arms in
`MethodMap.Array.cs`:

- `icall_array_get_long_length_dim(arr, dimension)` validates the dimension the
  same way `GetLength` does (`RequireDimensionInRange`) and returns
  `dims[dimension]` as `int64_t`.
- The three flag functions take the receiver through `RequireInstanceArray` so a
  null array still throws `NullReferenceException`, then answer the net48
  constants.

## Evidence

`tests/cases/probe-arrayapi` (12 lines), byte-identical to the net8 oracle
(`sha=9bb6d1b62eb40d6c0c3732f15028d871a01b565a870b579dfe54c82aa4a236ea`):

```
empty:0
exists:True
find:8
findidx:1
findlast:4
findall:3
trueall:True
resize:4:1:0
foreach:25
enum:25
longlen:5
flags:True:False:False
```

The first ten lines are the pre-existing surface, kept in the case so a
regression in the search family is caught by the same contract. `longlen` and
`flags` are the new members.

## Mutation evidence

| mutation | result |
|---|---|
| `icall_array_get_is_fixed_size` returns `false` | `flags:False:False:False` instead of `True:False:False`, output differs from the oracle |
| restored | 12/12 lines match, exit 0 |

## Regression

19 cases pass, including every array case in the tree.

## Still not covered

`Array.AsReadOnly<T>` is still unmapped: it returns a
`ReadOnlyCollection<T>`, which needs the collection type's own descriptor and
constructor. It is the one member of the `System.Array` public surface this
round did not close, and it fails loudly at transpile time rather than
producing a wrong result.
