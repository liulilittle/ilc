# GAPS.md -- culturefmt unwired/divering surface (e2e batch C58, 2026-08-12)

Case status: **PASS mingw + PASS msvc** (77 deterministic bool probes, all
True; net48 oracle == transpiled runtime byte-exact). The active probes cover
the full zh-CN CURRENCY / PERCENT / NUMBER matrices plus the G/F/E complement
through the formatext.cpp 15-digit round-half-away surface. The single gap
below was VERIFIED empirically (cmake build failure) and is recorded without
fixing, per batch rules.

## D-CUL-1 -- NumberFormatInfo.get_CurrencySymbol unwired

- **Phenomenon**: `NumberFormatInfo.CurrentInfo.CurrencySymbol` (the current
  instance is the null-folded `get_CurrentInfo` receiver) has no resolver
  entry -- MethodMap.NumberFormat.cs wires get_NumberDecimalSeparator /
  get_NumberGroupSeparator / get_PercentSymbol / get_PercentDecimalSeparator /
  get_PercentGroupSeparator / get_PositiveSign / get_NegativeSign /
  get_NaNSymbol / get_PositiveInfinitySymbol / get_NegativeInfinitySymbol but
  NOT get_CurrencySymbol (or get_CurrencyDecimalSeparator /
  get_CurrencyGroupSeparator / get_CurrencyDecimalDigits). The call falls
  through to the bogus generated-name path: the cmake build fails with `use of
  undeclared identifier
  'System_Globalization_NumberFormatInfo_get_CurrencySymbol_...'` at the
  callvirt site.
- **Impact**: the zh-CN currency symbol (U+00A5) is pinned ONLY through the
  ToString("C") surface here (probe `cur_in_fmt`), never through the getter --
  the culturedeep precedent (which also probed CurrencySymbol only via C).
- **Fix path (future)**: add `case "get_CurrencySymbol": return
  Literal("\u00A5")` to MethodMap.NumberFormat.cs (matching the zh-CN fixed
  culture), plus the other unwired currency getters if needed.
