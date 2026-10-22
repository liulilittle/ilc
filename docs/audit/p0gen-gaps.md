# p0gen gaps -- generic-method variant routing (observed while fixing the phantom symbols)

Scope: defects found while making generic-method variant emission and
resolution symmetric (`p0gen`). The variant-routing defect itself is FIXED;
what follows are the shapes that remain unsupported, recorded so a later pass
does not rediscover them. None of them are made worse by the fix -- they were
already uncompilable before it.

## 1. `constrained.` callvirt on a METHOD generic parameter (`T.ToString()`)

A generic method whose body calls an instance method ON the generic value is
type-DIRECTED, so it has no concrete slot variants (correctly: the variant body
would be uncompilable for a value slot). It binds the base definition symbol,
whose body is itself not compilable because the MVAR's `constrained.` dispatch
is not resolved to the concrete instantiation.

```csharp
public static class Phant1
{
    static string S<T>(T v) => v.ToString();

    public static void Main()
    {
        Console.WriteLine(S<int>(5));       // net48: "5" via Int32.ToString()
        Console.WriteLine(S<long>(7L));     // net48: "7"
        Console.WriteLine(S<string>("s"));  // net48: "s" (identity)
    }
}
```

```
error: cannot convert 'int32_t*' {aka 'int*'} to 'il2cpp::Il2CppObject*'
error: invalid conversion from 'il2cpp::Il2CppObject*' to 'int32_t' {aka 'int'}
```

- `cannot convert 'int32_t*' -> 'Il2CppObject*'`: the base body emits
  `icall_object_to_string((&arg0))` where `arg0` is the int32 default slot. The
  receiver is the ADDRESS of a value slot but the icall takes an object. net48
  semantics: `constrained. !!T callvirt Object::ToString()` dispatches to
  `Int32.ToString()` when `T` is `int`.
- `Il2CppObject* -> int32_t`: the `_string` call site passes a reference into
  the base definition's int32 parameter.

WHY NOT FIXED HERE: the correct fix needs per-instantiation dispatch of a
`constrained.` callvirt on an MVAR -- i.e. the concrete `T` has to reach the
body that today transpiles once with the int32 default. That is a
generic-instantiation closure problem, not a routing problem, and is out of
scope for `p0gen`.

WHY NOT LOUD: replacing the shape with a `NotSupportedException` was explicitly
forbidden for this task, and it would not satisfy the requirement either (it
turns "compile failure" into "explicit rejection" without producing the correct
`"5"`). Registered here instead.

## 2. Consequence: a type-directed generic method is a compile-time failure, not a phantom

Before `p0gen` the same source ALSO failed, but with the strictly worse shape:
the resolver appended `_long` / `_string` to a symbol that was never emitted
(`'Cases_Phant1_S_<hash>_long' was not declared in this scope`). After the fix
those two phantom errors are gone; the remaining two errors are the ones
described above -- they point at the real, already-existing gap.
