# GAPS.md -- p-adb-loud recorded findings (adm1 defect-fix batch: D2 false-LOUD)

Case status: **RUNTIME-CONTRACT probe** (NOT a byte-exact difftest -- net48 and
the runtime deliberately diverge here). Verification: transpile the probe and
run the transpiled runtime; the output must be exactly `[loud.tostring]=True`
(caught NotSupportedException). `expected.txt` pins that runtime contract.

Scope: the adm1 review's D2 defect -- `AppDomain.ToString()` at a REAL call
site must GENUINELY throw NotSupportedException.

## Fixed here

- The adm1 v1 LOUD arm (MethodMap.Legacy.cs `ToString` -> icall_appdomain_
  to_string) was DEAD at real call sites: Roslyn emits `callvirt
  System.Object::ToString` for the net48 AppDomain.ToString override, and the
  resolver path carries no tracked receiver, so the call reached the
  valuesemantics.cpp klass-name fallback and printed "System.AppDomain"
  instead of throwing (false-LOUD). Two complementary changes make the throw
  genuine and reachable:
  1. MethodMap.Object.cs routes `Object::ToString` on an AppDomain-tracked
     receiver to icall_appdomain_to_string (the EmitVirtualCall receiverType
     path).
  2. valuesemantics.cpp `icall_object_to_string` THROWS NotSupportedException
     when `obj->klass == &System_AppDomain_class` (the receiverType-less
     EmitCallStmt path -- the observed real path). The runtime has no
     localized name implementation, so the honest surface is the throw.
- Net48 returns the localized `名称:...` text; this probe asserts the adm1
  LOUD contract on the runtime side only (the classic divergence the batched
  difftest cannot represent byte-exact).

## Recorded, NOT fixed (W7)

- The localized `AppDomain.ToString()` text itself, `GetAssemblies` counts,
  and the CAS object values stay LOUD (the original adm1 W7 rows).