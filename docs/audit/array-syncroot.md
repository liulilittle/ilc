# Array.SyncRoot: the array itself, and the ReturnsRef flag

`Array.get_SyncRoot` had no mapping arm and failed at TRANSPILE time:

```
warning: method fold failed -> skipped (Program::Main: System.Array::get_SyncRoot:
System.Array::get_SyncRoot: not wired in M5 (BCL owner has no mapping arm and is
not defined in the input assembly -- a phantom generated-name call would never
link; receiverType='System.Int32[]' arg0Type='System.Int32[]'))
```

## The semantics

net48 answers the array ITSELF as the synchronization object -- an array has no
separate sync object. So the runtime function returns the receiver unchanged,
and the result is stable across calls (`ReferenceEquals(a.SyncRoot, a.SyncRoot)`
is true). A null array still throws `NullReferenceException`, which
`RequireInstanceArray` provides.

## The second defect, found by the first build

The first version of the mapping arm passed `ReturnsRef = false`:

```
return I("il2cpp::runtime::icall_array_get_sync_root", 0, true, false, true,
    returnTypeName: "System.Object");
```

`returnTypeName` does NOT decide the result temp's C++ type. `ExprKind` does,
and for a `CallExpr` the only arm that yields `"ref"` is
`CallExpr c when c.ReturnsRef => "ref"` (`CodeGenerator.cs:6413`). With
`ReturnsRef = false` the temp fell through to the `int32_t` default and the
generated C++ rejected the pointer assignment:

```
probe-arraysync.cs:10:54: error: invalid conversion from 'il2cpp::Il2CppObject*'
to 'int32_t' {aka 'int'}
probe-arraysync.cs:11:18: error: invalid operands of types 'int32_t' {aka 'int'}
and 'std::nullptr_t' to binary 'operator!='
```

The fix is the 4th `I()` flag: `ReturnsRef = true`.

## Evidence

`tests/cases/probe-arraysync` (4 lines), byte-identical to the net8 oracle
(`sha=a3ca1574d1eb0e475b9a58ef5121da5fd93eab32300bcc2f65987115e6a51688`):

```
root:True
self:True
stable:True
null:ok
```

`self:True` is the load-bearing line: it asserts the result IS the receiver, not
merely a non-null object. `stable:True` asserts the identity does not change
between calls. `null:ok` is the negative case.

## Mutation evidence

| mutation | result |
|---|---|
| `icall_array_get_sync_root` returns `nullptr` | `root:False` / `self:False` instead of `True`, output differs from the oracle |
| restored | 4/4 lines match, exit 0 |

## Regression

20 cases pass, including every array case in the tree.

## Still not covered

`Array.AsReadOnly<T>` is still unmapped. It returns a
`ReadOnlyCollection<T>`, which needs that collection type's own descriptor,
constructor and `IList<T>` implementation -- a collection-type task, not an
array one. It fails loudly at transpile time rather than producing a wrong
result.
