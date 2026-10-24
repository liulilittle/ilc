namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.TimeExt2 -- System.DateTime extension module 2 mappings
// (timeext2.h: ParseExact, tick arithmetic, static Compare).
// Owners: System.DateTime (TimeSpan: nothing -- timeext2.h has no TimeSpan
// entries; TimeSpan is fully covered by MethodMap.TimeSpan.cs).
//
// Naming contract (timeext2.h): every entry point is il2cpp_datetime_* --
// deliberately distinct from the icall_* names in il2cpp_time.h /
// il2cpp_time_ext.h, so no symbol collides. The runtime module EXTENDS the
// existing DateTime surface without touching it.
//
// Set-difference bookkeeping vs MethodMap.DateTime.cs (read-only, tried
// FIRST in the dispatch chain):
//   Parse(string)                -> il2cpp_datetime_parse              (NEW here)
//   Parse(string, IFormatProvider)-> il2cpp_datetime_parse             (NEW here;
//     the IFormatProvider arg is DROPPED at emission -- the runtime is
//     invariant-culture only; "drop" precedent: ToString(string, IFormatProvider))
//   ParseExact(2 args)          -> il2cpp_datetime_parse_exact      (NEW here)
//   ParseExact(3 args, provider)-> il2cpp_datetime_parse_exact      (NEW here;
//     the IFormatProvider arg is DROPPED at emission -- the runtime is
//     invariant-culture only; "drop" precedent: ToString(string, IFormatProvider))
//   AddTicks(long)              -> il2cpp_datetime_add_ticks        (NEW here)
//   SubtractTicks(long)         -> il2cpp_datetime_subtract_ticks   (NEW here)
//   Compare(DateTime, DateTime) -> il2cpp_datetime_compare          (NEW here)
//   ToString(format)            -> NOT wired here: MethodMap.DateTime.cs
//     already maps the 1- and 2-arg overloads to the canonical formatter
//     icall_datetime_to_string_format (il2cpp_time_ext.h). The ext2 twin
//     il2cpp_datetime_to_string_format delegates to the same formatter and
//     is deliberately left unwired -- a second mapping here would be
//     unreachable dead code (single-writer rule).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapTimeExt2(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.DateTime") return null;
        switch (name)
        {
            case "Parse":
                // DateTime.Parse(string input) -> DateTime: the general
                // invariant-culture subset (timeext2.h -- ISO "YYYY-MM-DD
                // [ T]HH:MM[:SS]" and US "M/D/YY[YY]" orders, TwoDigitYearMax
                // expansion, trimmed whitespace). The runtime icall takes
                // ONLY the input string; the net48 2-arg provider form drops
                // the provider at emission (ParamCount = the IL arity).
                if (ps.Length == 1)
                    return I("il2cpp::runtime::il2cpp_datetime_parse", 1, true, false, false, new[] { "" }, "dt");
                if (ps.Length == 2)
                    // DateTime.Parse(string, IFormatProvider): provider DROPPED
                    // at emission (the runtime is invariant-culture only --
                    // "drop" precedent: ParseExact 3-arg, ToString(string,
                    // IFormatProvider)). Every net48 Parse call site emits
                    // this shape (CultureInfo.InvariantCulture folds to null
                    // at the call site either way).
                    return I("il2cpp::runtime::il2cpp_datetime_parse", 2, true, false, false, new[] { "", "drop" }, "dt");
                throw new NotSupportedException($"DateTime.Parse with {ps.Length} arg(s) not wired: the runtime has only the (string) icall (no provider+styles overloads)");
            case "ParseExact":
                // ParseExact(string input, string format) -> DateTime.
                if (ps.Length == 2)
                    return I("il2cpp::runtime::il2cpp_datetime_parse_exact", 2, true, false, false, new[] { "", "" }, "dt");
                // ParseExact(string, string, IFormatProvider): the 3-arg
                // overload -- the provider arg is dropped at emission (the
                // runtime is invariant-culture only). "drop" precedent:
                // DateTime.ToString(string, IFormatProvider).
                if (ps.Length == 3)
                    return I("il2cpp::runtime::il2cpp_datetime_parse_exact", 3, true, false, false, new[] { "", "", "drop" }, "dt");
                // pdtx1 (2026-10-24): the 4-arg styles overloads --
                // (string, string, IFormatProvider, DateTimeStyles) and
                // (string, string[], IFormatProvider, DateTimeStyles). The
                // provider (0x12 CLASS) is dropped; the styles enum (0x11
                // VALUETYPE) rides the "i" cast; the format array (0x1D) rides
                // the "a" cast.
                if (ps.Length == 4 && ps[2] == 0x12 && ps[3] == 0x11)
                {
                    if (ps[1] == 0x0E)
                        return I("il2cpp::runtime::il2cpp_datetime_parse_exact_styles", 4, true, false, false,
                            new[] { "", "", "drop", "i" }, "dt");
                    if (ps[1] == 0x1D)
                        return I("il2cpp::runtime::il2cpp_datetime_parse_exact_multi", 4, true, false, false,
                            new[] { "", "a", "drop", "i" }, "dt");
                }
                throw new NotSupportedException($"DateTime.ParseExact with {ps.Length} arg(s) not wired: the runtime has (string,string)[,IFormatProvider[,DateTimeStyles]] and the string[] format-array forms");
            case "TryParseExact":
                // pdtx1 (2026-10-24): the 5-arg styles overloads --
                // (string, string, IFormatProvider, DateTimeStyles, out
                // DateTime) and the string[] format-array form. The provider is
                // dropped; the styles enum rides "i"; the out DateTime slot is
                // the managed local's ADDRESS (the "" passthrough).
                if (ps.Length == 5 && ps[2] == 0x12 && ps[3] == 0x11 && ps[4] == 0x11)
                {
                    if (ps[1] == 0x0E)
                        return I("il2cpp::runtime::il2cpp_datetime_try_parse_exact", 5, true, false, false,
                            new[] { "", "", "drop", "i", "" });
                    if (ps[1] == 0x1D)
                        return I("il2cpp::runtime::il2cpp_datetime_try_parse_exact_multi", 5, true, false, false,
                            new[] { "", "a", "drop", "i", "" });
                }
                throw new NotSupportedException($"DateTime.TryParseExact with {ps.Length} arg(s) not wired: the runtime has (string,string,provider,styles,out DateTime) and the string[] format-array form");
            case "AddTicks":
                // DateTime.AddTicks(long ticks) -> DateTime (100 ns units).
                return I("il2cpp::runtime::il2cpp_datetime_add_ticks", 1, true, false, true, new[] { "deref:dt", "l" }, "dt");
            case "SubtractTicks":
                // The tick-arithmetic companion (timeext2.h). .NET Framework
                // has no SubtractTicks member, so net48 IL can never emit this
                // name -- wired for completeness against the runtime surface.
                return I("il2cpp::runtime::il2cpp_datetime_subtract_ticks", 1, true, false, true, new[] { "deref:dt", "l" }, "dt");
            case "Compare":
                // DateTime.Compare(DateTime, DateTime): -1 / 0 / +1 (static).
                // DateTime value args pass by value, no cast (op_Subtraction
                // pattern in MethodMap.DateTime.cs).
                return I("il2cpp::runtime::il2cpp_datetime_compare", 2, true, false, false, new[] { "", "" });
            default: return null;
        }
    }
}
