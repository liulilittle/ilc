# r77 Shard1 Baseline Scan (g++)

Source: `E:\il2cpp\r77shard\r77shard1_shard1_errors.log`
Total lines: 34068
Total `error:` lines: 6593

## Category Breakdown

| Pattern | Count |
|---------|-------|
| `cannot initialize` | 1241 |
| `cannot cast` | 1146 |
| `no matching function` (nmf) | 993 |
| `undeclared` | 251 |
| `incompatible pointer` | 208 |
| `incompatible integer` | 11 |
| `cannot convert` | 75 |

## Sub-patterns relevant to nmf T1-T6

| Pattern | Count |
|---------|-------|
| `cannot-initialize\|Il2CppObject\*.*int32_t\|int32_t.*Il2CppObject\*` (broad) | 2567 |
| `cannot initialize` (exact) | 1241 |
| `no matching function` (nmf) | 993 |
| `undeclared` | 251 |
| `incompatible pointer` | 208 |

## Observations

- `no matching function` (993) is the primary nmf signature-mismatch signal.
- `cannot initialize` (1241) covers the family of int32_t* vs int32_t / Il2CppObject* vs int32_t slot-type mismatches.
- `undeclared` (251) indicates symbols absent from generated headers.
- `incompatible pointer` (208) covers pointer/int width mismatches (intptr_t* vs int32_t*).
- `cannot cast` (1146) + `cannot convert` (75) cover static_cast/reinterpret_cast failures.

## Sample error shapes (first 5 each)

### cannot initialize
```
Microsoft_Runtime_Hosting_StrongNameHelpers.cpp:176:263: error: cannot initialize a parameter of type 'int32_t *' (aka 'int *') with an lvalue of type 'intptr_t *' (aka 'long long *')
```

### no matching function
```
Microsoft_Runtime_Hosting_StrongNameHelpers.cpp:95:5: error: no matching function for call to 'System_Guid__ctor_1430675b62f51540'
Microsoft_Runtime_Hosting_StrongNameHelpers.cpp:99:42: error: no matching function for call to 'System_Runtime_InteropServices_RuntimeEnvironment_GetRuntimeInterfaceAsObject_9d9484173c3d0195'
```

### undeclared
(251 occurrences — symbols missing from generated headers)

## Goal

Reduce the sum of `cannot initialize` + `no matching function` + `undeclared` + `incompatible pointer` by fixing ClosedParamTypes null-fallthrough to `int32_t` in CodeGenerator.cs.
