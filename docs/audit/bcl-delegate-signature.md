# BCL delegate detection: signature, not a name list

CoreLib 8.0.31 / net48 reference assemblies. Supersedes the name-list approach
in `MethodRefResolver.IsBclDelegateType`.

## The defect

`newobj <DelegateType>::.ctor(object, native int)` must fold to
`il2cpp_delegate_new(&method, target)`. When the transpiler does not recognise
the type as a delegate, the ctor takes the reference-allocation path and emits
`<Type>__ctor_<hash>`, a symbol nothing defines. The phantom guard then rejects
it at transpile time (`not wired in M5`) or, for `publicapi` rows, the
transpile exits 0 and the C++ build fails.

The recognition was a hardcoded name list. It was incomplete:

| assembly | public delegates | named by the list | missing |
|---|---|---|---|
| mscorlib (net48) | 48 | 37 | **11** |
| System.dll (net48) | 66 | 2 | **64** |

Reproduced before the fix:

```
$ il2cppc cb60.exe gen
il2cppc error: P::Main: System_Reflection_MemberFilter__ctor_c99ae6aae6055830:
  not wired in M5 (BCL owner has no mapping arm; the generated name would never link)
il2cppc error: P::Main: System_ComponentModel_PropertyChangedEventHandler__ctor_e970c5cfdfebb034:
  not wired in M5 (...)
```

`PropertyChangedEventHandler` is the single most common event-handler delegate
in .NET; `MemberFilter` / `TypeFilter` / `ModuleResolveEventHandler` are the
reflection callback family. All were unusable.

## Why a name list cannot be fixed

`MetadataLoader.LoadAssembly(opts.InputAssembly)` loads **only the user
assembly**. mscorlib's and System.dll's delegate types are not in the model at
transpile time, so the transpiler cannot ask "does this type derive from
MulticastDelegate?". A name list is therefore a hand-maintained copy of a set
the compiler cannot see — it can only ever be partial, and it drifts.

Adding the 75 missing names would fix today's symptom and leave the cause: the
next assembly a user references (System.Core, System.Xml, ...) brings more
delegates, and each one is a new hole.

## The fix

Decide from the ctor **signature**, which is universal:

```
Void .ctor(System.Object, <native int>)
```

`StackFold.IsDelegateCtorSignature(md, token)` decodes the ctor blob and accepts
exactly that shape. It is consulted at the `newobj` delegate arm alongside the
existing name test:

```csharp
if (IsDelegateType(typeFullName, types)
    || (md != null && IsDelegateCtorSignature(md, (int)ins.Operand)))
```

The name test stays for the cases where the signature is not available (a
`TypeDef`-declared user delegate whose ctor is a `MethodDefinition`, and the
`Invoke`-site checks that have no ctor token at all).

### The IntPtr encoding trap

`csc` encodes `System.IntPtr` in a delegate ctor signature as
`ELEMENT_TYPE_I` (`0x18`, the native-int primitive), **not** as
`ELEMENT_TYPE_VALUETYPE` (`0x11`) + `TypeDefOrRef`. The first version of the
test only accepted `0x11` and therefore matched nothing:

```
type=System.ComponentModel.PropertyChangedEventHandler
  .ctor pc=2 ret=0x01 params=[0x1C, 0x18]
```

Both spellings mean IntPtr, so both are accepted.

### The one non-delegate with this shape

`System.Runtime.InteropServices.HandleRef` also has a
`Void .ctor(System.Object, System.IntPtr)`. It never reaches the delegate arm:
the value-type `newobj` arm claims it first (`StackFold.cs`, the
`isValueTypeNewobj` chain). Verified — `new HandleRef(...)` still emits
`il2cpp_object_new`, not `il2cpp_delegate_new`.

## Verification

| check | result |
|---|---|
| `MemberFilter`, `ModuleResolveEventHandler`, `PropertyChangedEventHandler`, `RemoteCertificateValidationCallback`, `MatchEvaluator` | all emit `il2cpp_delegate_new` (4 refs in one probe) |
| `HandleRef` | still `il2cpp_object_new` (no regression) |
| mutation: `if (true) return false;` in `IsDelegateCtorSignature` | `PropertyChangedEventHandler` fails again (exit 2) |
| restore | exit 0, `delegate_new=4` |
| regression | `corelibp0`, `probe-marshal-helpers`, `probe-pinvoke-error`, `comslot`, `threadops` all PASS |
| unit suite | 4173/4173 |

## The gate

`tools/audit/bcl_delegate_gate.py` re-derives the truth from metadata and fails
when any shipped delegate's ctor does not match the shape the test accepts. It
runs in CI as step 3.73.

`tools/audit/DelegateProbe` produces the input: every `TypeDef` whose direct
base is `System.MulticastDelegate`, with visibility and ctor shape.

```
$ python tools/audit/bcl_delegate_gate.py \
    --shipped-tsv mscorlib.tsv --shipped-tsv System.tsv
shipped_public_delegates=114
matched_by_signature=114
missed=0
VERDICT: SIGNATURE TEST COVERS ALL SHIPPED DELEGATES
```

Gate mutation: forcing the `obj,intptr` branch to never match reports
`missed=114` and exits 1.

## Scope and limits

- The gate covers mscorlib and System.dll, the two assemblies the net48
  reference set puts delegates in. A user assembly referencing another BCL
  assembly (System.Core, System.Xml, ...) is covered by the signature test at
  transpile time, but not by this gate's enumeration. Extending the gate is
  adding a `--shipped-tsv` argument.
- The signature test proves the *shape* is recognised, not that the resulting
  `il2cpp_delegate_new` call is semantically correct for every delegate family.
  Invocation ABI per family is a separate concern (see the `Sel1_*` /
  `DelegateInvokeExpr` work).
- `IsBclDelegateType` (the name list) is still used by `IsExternalBclOwner` to
  exempt delegates from the phantom guard. That list is now redundant for
  `newobj` but not for the guard, which sees only an owner name and no ctor
  token. Removing it entirely would require threading the ctor token into the
  guard; not done here.
