# Value-struct gaps observed while wiring delegate value-type generic args

Scope: pre-existing defects found during the `nb17` work that makes a delegate
`Invoke` accept a VALUETYPE generic argument (`Func<Guid,int>`, `Action<DateTime>`,
`Predicate<decimal>`, ...). None of them are introduced by that work, and none
are fixed here -- they are recorded so a later pass does not rediscover them.

## 1. `System.EventHandler`1<T>` isinst/castclass whitelist gap

`CodeGenerator.cs` gates an isinst/castclass on a TypeSpec (generic
instantiation) target behind a delegate-name whitelist. The list covers
`Func`N`, `Action`N`, `Predicate`1`, `Converter`2` and `Comparison`1`, but not
the generic `EventHandler`1`.

Reproduction (minimal): closing a handler over a generic event and composing it
with `+=`

```csharp
EventHandler<EventArgs> h = OnEvt;
h += OnEvt;      // Delegate.Combine, then castclass back to the delegate type
h(null, EventArgs.Empty);
```

observed at transpile time (exit 2):

```
il2cppc error: Cases.EhProbe::Main: isinst/castclass of generic instantiation
  System.EventHandler`1 not wired in M5
```

The non-generic `System.EventHandler` does not hit this path (it is a TypeToken,
not a TypeSpec, and is handled by `IsCustomDelegateType`), so only the `1 arity
is affected.

Note that adding the whitelist arm alone is not sufficient to make the feature
work: the identity arm returns `reinterpret_cast<Il2CppObject*>(operand)`, and a
full `EventHandler`1` implementation also needs the runtime descriptor wiring
for the closed instantiation. That is why this is recorded as a gap rather than
patched as a one-line whitelist edit.

## 2. BCL struct generic arguments have no `TypeCppName` arm -- diagnosability regression

`CodeGenerator.TypeCppName(string)` maps the known BCL value structs
(`System.Guid`, `System.DateTime`, `System.TimeSpan`, `System.Decimal`,
`System.DateTimeOffset`, `System.Numerics.BigInteger`, ...) and otherwise falls
back to `TypeEmitter.Sanitize(fullName)`, i.e. a mangled bare identifier:

```csharp
// CodeGenerator.cs -- the final arm of TypeCppName(string)
_ => TypeEmitter.Sanitize(fullName),
```

For a TRANSLATED user value type (`Cases.Pair`, `Cases.Color`) that is correct:
`generated.h` does define `struct Cases_Pair` / `struct Cases_Color`, so the
delegate helper and the leaf both name a real type.

For an UNREGISTERED BCL value type the mangled name does not exist anywhere.
Before this work the delegate signature decode rejected such a shape loudly;
now it resolves to the mangled name and the failure surfaces later, at the C++
compile step:

```
error: 'System_Runtime_InteropServices_HandleRef' was not declared in this scope
error: 'System_Numerics_Complex' has not been declared
```

Reproduced by closing a delegate over `System.Numerics.Complex` (transpile exit
0, cmake build exit 2). The result is still a hard build failure -- no
mislabelled binary is produced -- but the diagnostic quality regressed from a
transpiler-level "not supported" message to a raw C++ "not declared" error.

Distinguish this from `TypeEmitter.TypeCppName(TypeDef)`, a DIFFERENT function
(`TypeEmitter.cs:529`) that does fall back to `Il2CppObject*` for types outside
the translated set. The two share a name but not a contract.

A future fix would give `CodeGenerator.TypeCppName(string)` an explicit arm (or
a LOUD reject) for value types it cannot name, rather than emitting a mangled
identifier.

## 3. `ToString()` on a user struct

Calling `ToString()` on a user-defined struct routes to the runtime
`icall_object_to_string` with the struct ADDRESS, but that icall's parameter is
`Il2CppObject*`:

```csharp
public struct Pair { public int A; public int B; }
Console.WriteLine(p.ToString());
```

```
error: cannot convert 'Cases_Pair*' to 'il2cpp::Il2CppObject*'
```

This is pre-existing: the same source fails the same way at `e41dde2f` (before
the delegate value-type work) and is unrelated to delegate signatures -- it is
the object-model boundary for struct receivers, not the Invoke ABI. Worked
around in the probes by projecting scalar fields instead of calling
`ToString()` on the struct.
