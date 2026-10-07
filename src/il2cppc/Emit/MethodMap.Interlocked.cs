namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Interlocked -- System.Threading.Interlocked icall mappings
// (il2cpp_interlocked.h: increment/decrement/exchange/add/compare_exchange
// x int32/int64 + pointer forms).
// Owners: System.Threading.Interlocked
//
// REF PARAMETER CONVENTION (the core semantics: the value MUST write back):
// the ref argument arrives as an ldloca LocAddrExpr, which the emitter
// renders as `(&local)`; cast "" passes that ADDRESS through to the icall's
// int32_t*/int64_t* location parameter UNCAST -- the address expression IS
// the parameter (same convention as Monitor.Enter (object, ref bool) and
// Guid.TryParse (string, out Guid)). The runtime icall writes through the
// pointer, so the writeback is exact.
//
// NOTE (interlockedcase regression, 2026-08-05): "" is a pure passthrough
// in CodeGenerator.EmitIcallCallArgs. Commit b59c6d9 blanket-cast every
// ""-cast LocAddrExpr arg to (uint8_t*) for Guid's value slots and broke
// these int32_t*/int64_t* locations (matrix4 cmake_build). The blanket is
// gone -- Guid's byte-slot cast is now the explicit "g" code
// (MethodMap.Guid.cs); this domain stays "" and passes the typed address
// through. Do NOT re-add a blanket cast here.
//
// BYREF DISPATCH (G5 fix): ReadParamByte (MethodRefResolver.cs) consumes the
// BYREF (0x10) element byte and ps carries the ELEMENT type, so:
//   * Increment/Decrement (single byref param): ps[0] is 0x08=i4 -> int32
//     icall, 0x0A=i8 -> int64 icall -- both families wire exactly (the old
//     ps == [0x10] ambiguity is gone; a ref-long call no longer falls to
//     the int32 icall).
//   * Add/Exchange/CompareExchange (2-3 params): ps[0] is the byref element,
//     ps[1] (and ps[2]) the VALUE-param type -- both families wire exactly.
// Pointer forms (Exchange/CompareExchange(ref object, ...)): the pointer
// icalls take void**/void*; the object-slot address would need a "(void**)"
// cast the emitter has no code for -- LOUD-rejected (known-unwireable).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapInterlocked(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Increment":
            case "Decrement":
            {
                // Single BYREF param: ps[0] is the ELEMENT byte (0x08=i4 ->
                // int32 icall, 0x0A=i8 -> int64 icall -- G5 fix).
                var suffix = name == "Increment" ? "increment" : "decrement";
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I($"il2cpp::runtime::il2cpp_interlocked_{suffix}", 1, true, false, false, new[] { "" });
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I($"il2cpp::runtime::il2cpp_interlocked_{suffix}64", 1, true, false, false, new[] { "" }, "l");
                return null;
            }
            case "Add":
            case "ExchangeAdd":
                return ps.Length == 2 && ps[0] == 0x0A && ps[1] == 0x0A
                    ? I("il2cpp::runtime::il2cpp_interlocked_add64", 2, true, false, false, new[] { "", "l" }, "l")
                    : ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08
                    ? I("il2cpp::runtime::il2cpp_interlocked_add", 2, true, false, false, new[] { "", "i" })
                    : null;
            case "Exchange":
                return ps.Length == 2 && ps[0] == 0x0A && ps[1] == 0x0A
                    ? I("il2cpp::runtime::il2cpp_interlocked_exchange64", 2, true, false, false, new[] { "", "l" }, "l")
                    : ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08
                    ? I("il2cpp::runtime::il2cpp_interlocked_exchange", 2, true, false, false, new[] { "", "i" })
                    : ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_interlocked_exchange_pointer", 2, true, true, false,
                        new[] { "pobj", "" })
                    // netcore21/31/net5 audit closure: Exchange(ref IntPtr,
                    // IntPtr) -- the native-int slot swap rides the same
                    // pointer-width icall (the address of an intptr_t local
                    // IS void**-compatible; "" passthrough both slots).
                    : ps.Length == 2 && ps[0] == 0x18 && ps[1] == 0x18
                    ? I("il2cpp::runtime::il2cpp_interlocked_exchange_pointer", 2, true, true, false,
                        new[] { "", "" })
                    // P6: Interlocked Exchange(ref float/double, float/double):
                    : ps.Length == 2 && ps[0] is 0x0C or 0x0D && ps[1] is 0x0C or 0x0D
                    ? I("il2cpp::runtime::il2cpp_interlocked_exchange_double", 2, true, false, false, new[] { "", "d" }, "d")
                    // P6: Generic Exchange<T> -- the T-bearing params arrive as
                    // 0x1E (MVAR -- Exchange<T> is a generic METHOD on a
                    // non-generic type, e2e_il evidenced) or 0x13 (VAR -- the
                    // generic-TYPE convention); genArgs has the concrete type.
                    : ps.Length == 2 && ps[0] is 0x13 or 0x1E && genArgs is { Length: 1 }
                    ? (genArgs[0] switch
                    {
                        0x08 => I("il2cpp::runtime::il2cpp_interlocked_exchange", 2, true, false, false, new[] { "", "i" }),
                        0x0A => I("il2cpp::runtime::il2cpp_interlocked_exchange64", 2, true, false, false, new[] { "", "l" }, "l"),
                        0x0C or 0x0D => I("il2cpp::runtime::il2cpp_interlocked_exchange_double", 2, true, false, false, new[] { "", "d" }, "d"),
                        // EVERY reference-type encoding (uniform Il2CppObject*
                        // slot -> raw pointer swap): 0x0E string (the documented
                        // genArgs byte -- ArraySort precedent), 0x12 class/
                        // interface/delegate, 0x15 GenericInst, 0x1C object,
                        // 0x1D/0x1F sz/mvar arrays (ReadParamByte returns each
                        // marker byte after consuming the element).
                        0x0E or 0x12 or 0x15 or 0x1C or 0x1D or 0x1F => I("il2cpp::runtime::il2cpp_interlocked_exchange_pointer", 2, true, true, false, new[] { "pobj", "" }),
                        _ => null,
                    })
                    : null;
            case "CompareExchange":
                // netcore21/31 audit closure: CompareExchange(ref int, int,
                // int, out bool) -- the success-indicator overload (raw sig
                // 00-04-08-10-08-08-08-10-02). The wrapper returns the
                // original AND writes the found flag. MUST precede the 3-arg
                // ternary chain (a check after `return` is dead code).
                if (ps.Length == 4 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x02)
                    return I("il2cpp::runtime::icall_interlocked_compare_exchange_found", 4, true, false, false,
                        new[] { "", "i", "i", "addr:b" });
                return ps.Length == 3 && ps[0] == 0x0A && ps[1] == 0x0A && ps[2] == 0x0A
                    ? I("il2cpp::runtime::il2cpp_interlocked_compare_exchange64", 3, true, false, false, new[] { "", "l", "l" }, "l")
                    : ps.Length == 3 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08
                    ? I("il2cpp::runtime::il2cpp_interlocked_compare_exchange", 3, true, false, false, new[] { "", "i", "i" })
                    : ps.Length == 3 && ps[0] == 0x1C && ps[1] == 0x1C && ps[2] == 0x1C
                    ? I("il2cpp::runtime::il2cpp_interlocked_compare_exchange_pointer", 3, true, true, false,
                        new[] { "pobj", "", "" })
                    // netcore21/31/net5 audit closure: CompareExchange(ref
                    // IntPtr, IntPtr, IntPtr) -- the native-int CAS rides the
                    // pointer-width icall ("" passthrough all three slots).
                    : ps.Length == 3 && ps[0] == 0x18 && ps[1] == 0x18 && ps[2] == 0x18
                    ? I("il2cpp::runtime::il2cpp_interlocked_compare_exchange_pointer", 3, true, true, false,
                        new[] { "", "", "" })
                    // P6: Interlocked CompareExchange(ref float/double, ...):
                    : ps.Length == 3 && ps[0] is 0x0C or 0x0D && ps[1] is 0x0C or 0x0D && ps[2] is 0x0C or 0x0D
                    ? I("il2cpp::runtime::il2cpp_interlocked_compare_exchange_double", 3, true, false, false, new[] { "", "d", "d" }, "d")
                    // P6: Generic CompareExchange<T> -- same MVAR/VAR dual
                    // encoding as Exchange<T> above.
                    : ps.Length == 3 && ps[0] is 0x13 or 0x1E && genArgs is { Length: 1 }
                    ? (genArgs[0] switch
                    {
                        0x08 => I("il2cpp::runtime::il2cpp_interlocked_compare_exchange", 3, true, false, false, new[] { "", "i", "i" }),
                        0x0A => I("il2cpp::runtime::il2cpp_interlocked_compare_exchange64", 3, true, false, false, new[] { "", "l", "l" }, "l"),
                        0x0C or 0x0D => I("il2cpp::runtime::il2cpp_interlocked_compare_exchange_double", 3, true, false, false, new[] { "", "d", "d" }, "d"),
                    // Same reference-type encoding set as Exchange<T> above.
                    0x0E or 0x12 or 0x15 or 0x1C or 0x1D or 0x1F => I("il2cpp::runtime::il2cpp_interlocked_compare_exchange_pointer", 3, true, true, false, new[] { "pobj", "", "" }),
                    _ => null,
                })
                : null;
            case "Read":
                // Interlocked.Read(ref long) = atomic current-value load
                // (int64); net48 reference semantics on x64. The byref param
                // decodes to 0x0A (int64 element byte). Returns int64.
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::il2cpp_interlocked_read64", 1, true, false, false, new[] { "" }, "l");
                return null;
            case "SpeculationBarrier":
                // ICALL-C (2026-08-07): the runtime ships the empty
                // il2cpp_interlocked_speculation_barrier (il2cpp_interlocked.h)
                // -- a hardware speculation fence. ILC's atomics are all
                // seq_cst (Win32 Interlocked* / __ATOMIC_SEQ_CST) with no
                // speculative-load window, so the empty implementation is
                // semantically faithful (icallc2 report section 3). The old LOUD
                // rejection is replaced by the wiring.
                if (ps.Length == 0)
                    return I("il2cpp::runtime::il2cpp_interlocked_speculation_barrier", 0, false, false, false);
                return null;
            // netcore21->net7 audit gap closure: Interlocked.MemoryBarrier()
            // and ReadMemoryBarrier() -- full/acquire fences. The existing
            // icall_thread_memory_barrier is a std::atomic_thread_fence
            // (seq_cst), which SATISFIES both contracts (seq_cst is the
            // strongest ordering; an acquire fence never weakens a seq_cst
            // guarantee). One icall covers both names.
            case "MemoryBarrier":
            case "ReadMemoryBarrier":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_thread_memory_barrier", 0, false, false, false);
                return null;
            default:
                return null;
        }
    }
}
