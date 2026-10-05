# GAPS.md — typeops notes (2026-08-06)

23 probes PASS vs net48 (mingw, -O0). The WIRED Type surface holds: typeof
fold + .Name short names, GetType().Name, GetHashCode consistency (folded
Type values pool to ONE managed string per type -- same address, same hash),
object.ReferenceEquals and Type.op_Equality (==), all True/False-identical
to net48.

## DEFECT-TYPE-1 — System.Type.Equals(object) is not probeable in user code

MethodMap.Type.cs gates Type::Equals on a TRACKED System.Type argument
(`arg0Type == "System.Type"` -> icall_type_equals); anything else throws
"Type.Equals(object) with untracked not wired in M5". No user-code shape
produces a tracked Type argument today: a typeof fold is an UNTRACKED name
string, and a `Type` local variable is not tracked either. Verified with
dedicated probes -- `typeof(int).Equals(typeof(int))`,
`t1.Equals(t2)` (both Type locals) and `t1.Equals(typeof(string))` ALL abort
the transpile at StackFold.cs:1365. The Equals probes are NOT called here.

## DEFECT-TYPE-2/3/4 — Type.FullName / Type.IsValueType / Type.IsEnum are not wired

MethodMap.Type.cs has no get_FullName / get_IsValueType / get_IsEnum mapping.
Each member falls through to the generated-name path and emits an UNDEFINED
symbol (`System_Type_get_FullName_8048643721f03050`,
`System_Type_get_IsValueType_65a26cfaa8243411`,
`System_Type_get_IsEnum_53a23ea36b66776f`); the build aborts with "use of
undeclared identifier" (same silent-transpile / link-fail mechanism as
fileops2 DEFECT-FILE-1..5). These probes are NOT called here.
