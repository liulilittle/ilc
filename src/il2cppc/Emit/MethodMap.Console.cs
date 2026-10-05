namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Console -- System.Console icall mappings (il2cpp_console.h).
// Owners: System.Console
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapConsole(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (name == "WriteLine")
        {
            if (ps.Length == 0) return I("il2cpp::runtime::icall_console_write_line_n", 0, false, false, false, null, null, false, false, true);
            // D5 (surface 4): a SHARED method-level generic method's MVAR
            // return lands in the caller slot as the BOXED object
            // (Il2CppObject*). A subsequent `Console.WriteLine(<that slot>)`
            // whose IL signature is the VALUE type (WriteLine(int) after
            // `int y = Pick<int>(ref x)`) must UNBOX by the tracked box type
            // -- the arg expression is an object, the icall takes an int32/
            // double/... . Only fires when arg0Type names a tracked boxed
            // value (a genuine int local is never objTypes-tracked), so the
            // plain value-path stays byte-identical.
            if (ps[0] is 0x08 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x02 or 0x03
                && arg0Type is "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
                    or "System.Double" or "System.Single" or "System.Boolean" or "System.Char")
            {
                var boxedCast = arg0Type switch
                {
                    "System.Int32" => "unbox:i",
                    "System.UInt32" => "unbox:ui",
                    "System.Int64" => "unbox:l",
                    "System.UInt64" => "unbox:ul",
                    "System.Double" or "System.Single" => "unbox:d",
                    "System.Boolean" => "unbox:b",
                    _ => "unbox:c",
                };
                return ps[0] switch
                {
                    0x08 => I("il2cpp::runtime::icall_console_write_line_i", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    0x09 => I("il2cpp::runtime::icall_console_write_line_u32", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    0x0A => I("il2cpp_console_write_line_l", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    0x0B => I("il2cpp::runtime::icall_console_write_line_u64", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    0x0D => I("il2cpp::runtime::icall_console_write_line_d", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    0x0C => I("il2cpp::runtime::icall_console_write_line_d", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    0x02 => I("il2cpp::runtime::icall_console_write_line_b", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                    _ => I("il2cpp::runtime::icall_console_write_line_c", 1, false, false, false, new[] { boxedCast }, null, false, false, true),
                };
            }
            return ps[0] switch
            {
                0x08 => I("il2cpp::runtime::icall_console_write_line_i", 1, false, false, false, null, null, false, false, true),
                // WriteLine(uint) (convcase L60/L61): the full-range unsigned
                // decimal -- 0xFFFFFFFF prints "4294967295", never the signed
                // printer's -1 (the "u" arm mirrors the 0x0B ulong precedent;
                // the arg's int32 bit pattern converts to uint32 modulo).
                0x09 => I("il2cpp::runtime::icall_console_write_line_u32", 1, false, false, false, null, null, false, false, true),
                0x0E => I("il2cpp::runtime::icall_console_write_line_s", 1, false, false, false, null, null, false, false, true),
                0x02 => I("il2cpp::runtime::icall_console_write_line_b", 1, false, false, false, null, null, false, false, true),
                0x03 => I("il2cpp::runtime::icall_console_write_line_c", 1, false, false, false, null, null, false, false, true),
                0x0D => I("il2cpp::runtime::icall_console_write_line_d", 1, false, false, false, null, null, false, false, true),
                // M5 wiring: WriteLine(float) -- float values live in double
                // slots; the double formatter renders them (documented
                // deviation: .NET G15 prints the float's own precision).
                0x0C => I("il2cpp::runtime::icall_console_write_line_d", 1, false, false, false, new[] { "d" }, null, false, false, true),
                // M5 wiring: WriteLine(object) with a TRACKED box type -- the
                // value is unboxed through the matching runtime helper; a
                // tracked StringBuilder renders via its ToString icall.
                0x1C or 0x12 when arg0Type is not null => arg0Type switch
                {
                    "System.Double" => I("il2cpp::runtime::icall_console_write_line_d", 1, false, false, false, new[] { "unbox:d" }),
                    "System.Single" => I("il2cpp::runtime::icall_console_write_line_d", 1, false, false, false, new[] { "unbox:d" }),
                    "System.Int32" => I("il2cpp::runtime::icall_console_write_line_i", 1, false, false, false, new[] { "unbox:i" }),
                    "System.Boolean" => I("il2cpp::runtime::icall_console_write_line_b", 1, false, false, false, new[] { "unbox:b" }),
                    "System.Char" => I("il2cpp::runtime::icall_console_write_line_c", 1, false, false, false, new[] { "unbox:c" }),
                    "System.Text.StringBuilder" => I("il2cpp::runtime::icall_console_write_line_s", 1, false, false, false, new[] { "tostring:sb" }),
                    // typeof-chain wiring (StackFold): the folded Type VALUE
                    // is the full-name string, and a bare string literal
                    // through the object overload is a string -- both route
                    // to write_line_s (net48 prints the type's full name).
                    "System.String" => I("il2cpp::runtime::icall_console_write_line_s", 1, false, false, false, null, null, false, false, true),
                    // enum box wiring (timecase work order 2, box milestone): a boxed
                    // ENUM value (box System.DayOfWeek etc.) routes through
                    // il2cpp_enum_to_string (enumext.h:148) and prints via
                    // write_line_s -- net48 prints the member NAME ("Monday").
                    // The "enum:l" cast (CodeGenerator EmitIcallCallArgs)
                    // emits il2cpp_enum_to_string(nullptr, unbox:l): the null
                    // class descriptor takes the documented Int32 no-metadata
                    // path (numeric decimal) until the register emission +
                    // name-bearing descriptor land (the same forward ABI as
                    // the Enum.ToString mapping, MethodMap.Enum.cs) -- the
                    // named-value path becomes reachable once the enum
                    // metadata is registered (il2cpp_enum_register) and the
                    // box carries a name-bearing class.
                    // Whitelist-else safety: the arg0Type switch only sees
                    // TRACKED box types (StackFold objTypes). Boxed PRIMS
                    // without a safe unbox cast stay LOUD below (their boxes
                    // are not int64 carriers -- the runtime's unbox_long
                    // klass-checks, so a wrong path would be a runtime
                    // InvalidCast instead of a compile-time error; boxed
                    // Int64 would also truncate through the Int32 numeric
                    // fallback). Non-enum user structs are LOUD-rejected
                    // earlier at the box_ switch (CodeGenerator) -- so the
                    // fall-through arm is exactly the enum family.
                    _ when arg0Type is "System.Int64" or "System.UInt64" or "System.UInt32"
                        or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" =>
                        throw new NotSupportedException($"Console.WriteLine(object) with box type {arg0Type} not wired in M5 (boxed prim without an int64-carrier unbox cast stays loud)"),
                    _ => I("il2cpp::runtime::icall_console_write_line_s", 1, false, false, false, new[] { "enum:l" }, null, false, false, true),
                },
                // M7 hashtable2/hashtablecase (2026-08-09): WriteLine(object)
                // with an UNTRACKED box -- the runtime object printer
                // icall_console_write_line_object (il2cpp_console.h:89) now
                // exists: it calls icall_object_to_string on the boxed value
                // (boxed primitives print their VALUE text, strings print
                // themselves, other objects print the type's full name; null
                // prints just the line terminator -- net48 TextWriter
                // WriteLine(object) semantics). The value arg rides through
                // as the reference slot it already is (no cast).
                0x1C or 0x12 => I("il2cpp::runtime::icall_console_write_line_object", 1, false, false, false, null, null, false, false, true),
                // M5 wiring: WriteLine(long) formats via the generated-header
                // inline helper (il2cpp_console_write_line_l: int64_to_string
                // + the string printer -- no runtime int64 console icall).
                0x0A => I("il2cpp_console_write_line_l", 1, false, false, false, new[] { "l" }, null, false, false, true),
                // WriteLine(ulong) (box diagnostic work order 4): full-range decimal via
                // the runtime icall (console_format_u64 -- the UInt64ToString
                // digit loop): UInt64.MaxValue prints "18446744073709551615",
                // never the int64 helper's -1. The "q" cast reinterprets the
                // 8-byte slot bit-preservingly (CodeGenerator cast table).
                0x0B => I("il2cpp::runtime::icall_console_write_line_u64", 1, false, false, false, new[] { "q" }, null, false, false, true),
                _ => throw new NotSupportedException($"Console.WriteLine param type 0x{ps[0]:X2} not wired in M5"),
            };
        }
        if (name == "Write")
        {
            if (ps.Length == 0) throw new NotSupportedException("Console.Write() (no args) does not exist");
            return ps[0] switch
            {
                0x08 => I("il2cpp::runtime::icall_console_write_i", 1, false, false, false, null, null, false, false, true),
                // Write(uint) (convcase probe): the full-range unsigned
                // decimal -- 0xFFFFFFFF prints "4294967295", never the signed
                // printer's -1 -- via the no-newline u32 icall (the
                // write_line_u32 variant; the arg's int32 bit pattern
                // converts to uint32 modulo).
                0x09 => I("il2cpp::runtime::icall_console_write_u32", 1, false, false, false, null, null, false, false, true),
                0x0E => I("il2cpp::runtime::icall_console_write_s", 1, false, false, false, null, null, false, false, true),
                0x02 => I("il2cpp::runtime::icall_console_write_b", 1, false, false, false, null, null, false, false, true),
                0x03 => I("il2cpp::runtime::icall_console_write_c", 1, false, false, false, null, null, false, false, true),
                0x0D => I("il2cpp::runtime::icall_console_write_d", 1, false, false, false, null, null, false, false, true),
                _ => throw new NotSupportedException($"Console.Write param type 0x{ps[0]:X2} not wired in M5"),
            };
        }
        if (name == "ReadLine")
            return I("il2cpp::runtime::icall_console_read_line", 0, true, true, false);
        if (name == "ReadKey")
            return I("il2cpp::runtime::icall_console_read_key", 1, true, false, false, new[] { "i" });
        // console3 (2026-08-09): Console.Error / Console.Out -- the CONSOLE
        // STREAM objects (console.cpp ConsoleStreamObject: header + int32
        // kind). The getters return the CURRENT stream (swapped by SetOut /
        // SetError); the returned reference is tracked under the distinct
        // pseudo-type, which the TextWriter member dispatch (MethodRefResolver.cs
        // TextWriter block) routes to icall_console_stream_write[_line] -- the
        // receiver's concrete tracked type disambiguates a Console stream from
        // StringIO/StreamIO wrappers (the Encoding-domain precedent: the folded
        // singleton getter carries ReturnTypeName and the consumer dispatches on
        // it).
        if (name == "get_Error")
            return I("il2cpp::runtime::icall_console_get_error", 0, true, true, false,
                returnTypeName: "System.Console.ErrorStream");
        if (name == "get_Out")
            return I("il2cpp::runtime::icall_console_get_out", 0, true, true, false,
                returnTypeName: "System.Console.OutStream");
        if (name == "SetOut")
            return I("il2cpp::runtime::icall_console_set_out", 1, false, false, false, new[] { "" });
        if (name == "SetError")
            return I("il2cpp::runtime::icall_console_set_error", 1, false, false, false, new[] { "" });
        if (name == "get_IsInputRedirected")
            throw new NotSupportedException("Console.IsInputRedirected not wired: the runtime has no icall_console_is_input_redirected");
        return null;
    }

    // Console STREAM TextWriter members -- the Write/WriteLine surface of the
    // console stream objects (Console.Out / Console.Error / TextWriter.Null,
    // console.cpp icall_console_stream_write[_line]). The receiver (the stream
    // object) passes through as arg[0] with the "inst" shape (the existing
    // StringIO object-icall convention); the explicit text arg passes as-is
    // (Il2CppObject*; the runtime encodes UTF-8 internally). A null text is a
    // no-op (net48 TextWriter.Write/WriteLine(null)); WriteLine() writes just
    // the terminator.
    private static ResolvedMethod? TryMapConsoleStream(string owner, string name, byte[] ps, int count)
    {
        if (name == "Write")
        {
            if (count == 1 && ps[0] == 0x0E)
                return I("il2cpp::runtime::icall_console_stream_write", 1, false, false, true);
            throw new NotSupportedException(
                $"console stream TextWriter.Write overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired: " +
                "the console-stream icall takes exactly (self, string)");
        }
        if (name == "WriteLine")
        {
            if (count == 0)
                return I("il2cpp::runtime::icall_console_stream_write_line", 0, false, false, true);
            if (count == 1 && ps[0] == 0x0E)
                return I("il2cpp::runtime::icall_console_stream_write_line", 1, false, false, true);
            throw new NotSupportedException(
                $"console stream TextWriter.WriteLine overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired: " +
                "the console-stream icall takes exactly (self[, string])");
        }
        throw new NotSupportedException($"console stream TextWriter member '{name}' not wired (Write/WriteLine only)");
    }
}
