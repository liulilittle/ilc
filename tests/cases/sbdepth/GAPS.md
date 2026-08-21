# GAPS.md — sbdepth unwired surface (e2e batch C68, 2026-08-12)

The case probes ONLY the wired StringBuilder surface (65 lines PASS vs net48,
dual-compiler mingw+msvc). Gaps below were verified before being excluded.

## D-SBD-1 — AppendFormat forms outside (string, object[,object[,object]]) are unwired

- **Phenomenon**: `StringBuilder.AppendFormat(string, params object[])`, the
  `IFormatProvider` overloads and the `Decimal` argument dispatch are NOT
  wired (MethodMap.StringBuilder.cs: the AppendFormat case requires
  `ps[0] == 0x0E && ps[1] == 0x1C` -- a params object[] first arg surfaces as
  0x1D and throws NotSupportedException LOUD). sbdepth therefore limits
  AppendFormat args to the composite-engine dispatch set (boxed
  Int32/Int64/Double/Single + string + other objects via
  icall_object_to_string).
- **Suspected root file**: MethodMap.StringBuilder.cs AppendFormat case
  (only fixed arities 2/3/4 wired); runtime icall_stringbuilder_append_format
  family in stringbuilder.cpp.

## D-SBD-2 — set_Capacity / set_Length / Chars indexer / MaxCapacity unwired

- **Phenomenon**: `StringBuilder.set_Capacity`, `set_Length`, the `Chars[int]`
  indexer and `MaxCapacity` have no MethodMap arm (get_Capacity reads the
  runtime struct field; get_Length is the only mutating-adjacent surface
  wired). sbdepth therefore asserts capacity ONLY through get_Capacity after
  growth/remove/insert/replace -- never through direct mutation.
- **Suspected root file**: MethodMap.StringBuilder.cs (no set_Capacity /
  set_Length / get_Chars arms) + il2cpp_stringbuilder.h.

## D-SBD-3 — ctor(string, int) and the default-culture AppendFormat divergence

- **Phenomenon**: `StringBuilder(string, int capacity)` is not wired (ctor
  dispatch covers 0/int/string only). Also, AppendFormat's culture-sensitive
  output ("C"/"P"/"N" through the composite engine) follows the runtime's
  zh-CN fixing; sbdepth pins only the culture-stable specs (D/X/F/N decimal
  separators/grouping match zh-CN).
- **Suspected root file**: MethodMap.StringBuilder.cs ctor dispatch.
