namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Console -- System.Console icall mappings (il2cpp_console.h).
// Owners: System.Console
//
// SURFACE (misc3 full-overload alignment): every net48 Console.Write /
// WriteLine overload routes to a REAL runtime formatter, never an LOUD
// reject:
//   WriteLine()                      -> icall_console_write_line_n
//   WriteLine(bool|char|int|uint|long|ulong|double|float|string|object)
//                                    -> icall_console_write_line_{b,c,i,u32,l,
//                                       u64,d,s,object}
//   WriteLine(decimal)               -> icall_console_write_line_decimal
//   WriteLine(char[])                -> icall_console_write_line_chars
//   WriteLine(char[], int, int)      -> icall_console_write_line_chars_range
//   WriteLine(string, object...)     -> icall_console_write_line_fmt{1,2,3}
//   WriteLine(string, object[])      -> icall_console_write_line_fmt_array
//   Write(...)                       -- the same family without a newline
//   ReadLine / ReadKey / Read / In   -- the input surface
//   Out / Error (+SetOut/SetError)   -- the console stream objects
//   IsInputRedirected / IsOutputRedirected / IsErrorRedirected
//   Clear / Beep / Title / ForegroundColor / BackgroundColor / ResetColor /
//   CursorLeft / CursorTop / SetCursorPosition / WindowWidth / WindowHeight /
//   BufferWidth / BufferHeight       -- the console-device surface
//
// D5 (surface 4) retained: a SHARED method-level generic method's MVAR return
// lands in the caller slot as the BOXED object (Il2CppObject*). A subsequent
// `Console.WriteLine(<that slot>)` whose IL signature is the VALUE type
// (WriteLine(int) after `int y = Pick<int>(ref x)`) must UNBOX by the tracked
// box type -- only fires when arg0Type names a tracked boxed value.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapConsole(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string? paramTypes = null)
    {
        if (name == "WriteLine")
            return MapConsoleWrite(ps, count, arg0Type, paramTypes, newline: true);
        if (name == "Write")
            return MapConsoleWrite(ps, count, arg0Type, paramTypes, newline: false);
        if (name == "ReadLine")
            return I("il2cpp::runtime::icall_console_read_line", 0, true, true, false);
        if (name == "Read")
            return I("il2cpp::runtime::icall_console_in_read", 0, true, false, false);
        if (name == "Peek")
            return I("il2cpp::runtime::icall_console_in_peek", 0, true, false, false);
        if (name == "ReadKey")
            return I("il2cpp::runtime::icall_console_read_key", 1, true, false, false, new[] { "i" });
        // console3 (2026-08-09): Console.Error / Console.Out -- the CONSOLE
        // STREAM objects (console.cpp ConsoleStreamObject: header + int32
        // kind). The getters return the CURRENT stream (swapped by SetOut /
        // SetError); the returned reference is tracked under the distinct
        // pseudo-type, which the TextWriter member dispatch (MethodRefResolver.cs
        // TextWriter block) routes to icall_console_stream_write[_line] -- the
        // receiver's concrete tracked type disambiguates a Console stream from
        // StringIO/StreamIO wrappers.
        if (name == "get_Error")
            return I("il2cpp::runtime::icall_console_get_error", 0, true, true, false,
                returnTypeName: "System.Console.ErrorStream");
        if (name == "get_Out")
            return I("il2cpp::runtime::icall_console_get_out", 0, true, true, false,
                returnTypeName: "System.Console.OutStream");
        // Console.In: the standard-input TextReader object (console.cpp
        // ConsoleInObject). The tracked pseudo-type routes Read/Peek/ReadLine/
        // ReadToEnd to the stdin icalls (MethodMap.Console.cs TryMapConsoleStream).
        if (name == "get_In")
            return I("il2cpp::runtime::icall_console_get_in", 0, true, true, false,
                returnTypeName: "System.Console.InReader");
        if (name == "SetOut")
            return I("il2cpp::runtime::icall_console_set_out", 1, false, false, false, new[] { "" });
        if (name == "SetError")
            return I("il2cpp::runtime::icall_console_set_error", 1, false, false, false, new[] { "" });
        if (name == "get_IsInputRedirected")
            return I("il2cpp::runtime::icall_console_is_input_redirected", 0, true, false, false);
        if (name == "get_IsOutputRedirected")
            return I("il2cpp::runtime::icall_console_is_output_redirected", 0, true, false, false);
        if (name == "get_IsErrorRedirected")
            return I("il2cpp::runtime::icall_console_is_error_redirected", 0, true, false, false);
        // ---- console device surface (console_device.cpp) ----
        if (name == "Clear")
            return I("il2cpp::runtime::icall_console_clear", 0, false, false, false);
        if (name == "Beep")
            return count switch
            {
                0 => I("il2cpp::runtime::icall_console_beep", 0, false, false, false),
                2 => I("il2cpp::runtime::icall_console_beep_freq", 2, false, false, false, new[] { "i", "i" }),
                _ => null,
            };
        if (name == "get_Title")
            return I("il2cpp::runtime::icall_console_get_title", 0, true, true, false);
        if (name == "set_Title")
            return I("il2cpp::runtime::icall_console_set_title", 1, false, false, false, new[] { "s" });
        if (name == "get_ForegroundColor")
            return I("il2cpp::runtime::icall_console_get_foreground_color", 0, true, false, false);
        if (name == "set_ForegroundColor")
            return I("il2cpp::runtime::icall_console_set_foreground_color", 1, false, false, false, new[] { "i" });
        if (name == "get_BackgroundColor")
            return I("il2cpp::runtime::icall_console_get_background_color", 0, true, false, false);
        if (name == "set_BackgroundColor")
            return I("il2cpp::runtime::icall_console_set_background_color", 1, false, false, false, new[] { "i" });
        if (name == "ResetColor")
            return I("il2cpp::runtime::icall_console_reset_color", 0, false, false, false);
        if (name == "get_CursorLeft")
            return I("il2cpp::runtime::icall_console_get_cursor_left", 0, true, false, false);
        if (name == "set_CursorLeft")
            return I("il2cpp::runtime::icall_console_set_cursor_left", 1, false, false, false, new[] { "i" });
        if (name == "get_CursorTop")
            return I("il2cpp::runtime::icall_console_get_cursor_top", 0, true, false, false);
        if (name == "set_CursorTop")
            return I("il2cpp::runtime::icall_console_set_cursor_top", 1, false, false, false, new[] { "i" });
        if (name == "SetCursorPosition")
            return I("il2cpp::runtime::icall_console_set_cursor_position", 2, false, false, false, new[] { "i", "i" });
        if (name == "get_WindowWidth")
            return I("il2cpp::runtime::icall_console_get_window_width", 0, true, false, false);
        if (name == "get_WindowHeight")
            return I("il2cpp::runtime::icall_console_get_window_height", 0, true, false, false);
        if (name == "get_BufferWidth")
            return I("il2cpp::runtime::icall_console_get_buffer_width", 0, true, false, false);
        if (name == "get_BufferHeight")
            return I("il2cpp::runtime::icall_console_get_buffer_height", 0, true, false, false);
        if (name == "get_CursorVisible")
            return I("il2cpp::runtime::icall_console_get_cursor_visible", 0, true, false, false);
        if (name == "set_CursorVisible")
            return I("il2cpp::runtime::icall_console_set_cursor_visible", 1, false, false, false, new[] { "i" });
        return null;
    }

    // The shared Write/WriteLine overload dispatcher: `newline` selects the
    // icall family. `paramTypes` is the decoded signature text used only to
    // name a VALUETYPE argument (0x11 shares decimal/DateTime/enum/struct).
    private static ResolvedMethod? MapConsoleWrite(byte[] ps, int count, string? arg0Type, string? paramTypes,
        bool newline)
    {
        // Value-type argument naming for the 0x11 (VALUETYPE) shapes.
        string? vtName = null;
        if (paramTypes is { Length: > 0 })
        {
            var parts = CodeGenerator.SplitTopLevel(paramTypes);
            if (parts.Length > 0 && parts[0].StartsWith("V:", StringComparison.Ordinal))
                vtName = parts[0][2..];
        }
        if (count == 0)
        {
            // net48 has NO Console.Write() overload (only WriteLine()); the
            // IL can never produce this shape. Reaching it is a transpiler
            // bug, not a user-program gap -- keep it an internal error.
            return newline
                ? I("il2cpp::runtime::icall_console_write_line_n", 0, false, false, false, null, null, false, false, true)
                : throw new NotSupportedException("Console.Write() with zero arguments does not exist in net48 (internal transpiler error)");
        }
        if (count == 1)
        {
            // D5 (surface 4): a shared generic's MVAR slot arrives BOXED; the
            // IL signature is the VALUE type, so unbox by the tracked box type.
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
                var casts = new[] { boxedCast };
                return ps[0] switch
                {
                    0x08 => I(newline ? "il2cpp::runtime::icall_console_write_line_i" : "il2cpp::runtime::icall_console_write_i", 1, false, false, false, casts, null, false, false, true),
                    0x09 => I(newline ? "il2cpp::runtime::icall_console_write_line_u32" : "il2cpp::runtime::icall_console_write_u32", 1, false, false, false, casts, null, false, false, true),
                    0x0A => I(newline ? "il2cpp::runtime::icall_console_write_line_l" : "il2cpp::runtime::icall_console_write_l", 1, false, false, false, casts, null, false, false, true),
                    0x0B => I(newline ? "il2cpp::runtime::icall_console_write_line_u64" : "il2cpp::runtime::icall_console_write_u64", 1, false, false, false, casts, null, false, false, true),
                    0x0D => I(newline ? "il2cpp::runtime::icall_console_write_line_d" : "il2cpp::runtime::icall_console_write_d", 1, false, false, false, casts, null, false, false, true),
                    0x0C => I(newline ? "il2cpp::runtime::icall_console_write_line_d" : "il2cpp::runtime::icall_console_write_d", 1, false, false, false, casts, null, false, false, true),
                    0x02 => I(newline ? "il2cpp::runtime::icall_console_write_line_b" : "il2cpp::runtime::icall_console_write_b", 1, false, false, false, casts, null, false, false, true),
                    _ => I(newline ? "il2cpp::runtime::icall_console_write_line_c" : "il2cpp::runtime::icall_console_write_c", 1, false, false, false, casts, null, false, false, true),
                };
            }
            switch (ps[0])
            {
                case 0x08:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_i" : "il2cpp::runtime::icall_console_write_i", 1, false, false, false, null, null, false, false, true);
                // uint: the full-range unsigned decimal (0xFFFFFFFF prints
                // "4294967295", never -1) -- the u32 arm.
                case 0x09:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_u32" : "il2cpp::runtime::icall_console_write_u32", 1, false, false, false, null, null, false, false, true);
                case 0x0E:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_s" : "il2cpp::runtime::icall_console_write_s", 1, false, false, false, null, null, false, false, true);
                case 0x02:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_b" : "il2cpp::runtime::icall_console_write_b", 1, false, false, false, null, null, false, false, true);
                case 0x03:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_c" : "il2cpp::runtime::icall_console_write_c", 1, false, false, false, null, null, false, false, true);
                case 0x0D:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_d" : "il2cpp::runtime::icall_console_write_d", 1, false, false, false, null, null, false, false, true);
                // Single values live in double slots; the double formatter
                // renders them (documented deviation: .NET G15 prints the
                // float's own precision).
                case 0x0C:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_d" : "il2cpp::runtime::icall_console_write_d", 1, false, false, false, new[] { "d" }, null, false, false, true);
                // long / ulong: real runtime icalls (full int64/uint64 range).
                case 0x0A:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_l" : "il2cpp::runtime::icall_console_write_l", 1, false, false, false, new[] { "l" }, null, false, false, true);
                case 0x0B:
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_u64" : "il2cpp::runtime::icall_console_write_u64", 1, false, false, false, new[] { "q" }, null, false, false, true);
                // VALUETYPE: only the ones the console surface can render
                // through an existing formatter are wired; the decoded
                // signature text names them.
                case 0x11 when vtName == "System.Decimal":
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_decimal" : "il2cpp::runtime::icall_console_write_decimal", 1, false, false, false, new[] { "dec" }, null, false, false, true);
                // char[] overloads (single-dim array byte; the runtime
                // validates the element stride).
                case 0x1D:
                    return count == 1
                        ? I(newline ? "il2cpp::runtime::icall_console_write_line_chars" : "il2cpp::runtime::icall_console_write_chars", 1, false, false, false, new[] { "a" }, null, false, false, true)
                        : null;
                // object / class: the runtime object printer renders every
                // boxed primitive, string, StringBuilder and user override;
                // a null value writes nothing (Write) / just the terminator
                // (WriteLine) -- net48 TextWriter semantics.
                case 0x1C or 0x12:
                    if (arg0Type == "System.Text.StringBuilder")
                        return I(newline ? "il2cpp::runtime::icall_console_write_line_s" : "il2cpp::runtime::icall_console_write_s", 1, false, false, false, new[] { "tostring:sb" }, null, false, false, true);
                    if (arg0Type == "System.String")
                        return I(newline ? "il2cpp::runtime::icall_console_write_line_s" : "il2cpp::runtime::icall_console_write_s", 1, false, false, false, null, null, false, false, true);
                    return I(newline ? "il2cpp::runtime::icall_console_write_line_object" : "il2cpp::runtime::icall_console_write_object", 1, false, false, false, null, null, false, false, true);
            }
            throw new NotSupportedException(
                $"Console.{(newline ? "WriteLine" : "Write")} param type 0x{ps[0]:X2}" +
                (vtName != null ? $" ({vtName})" : "") + " not wired in M5");
        }
        // Composite forms: WriteLine(string format, object...) and the params
        // array -- routed through the String.Format kernel so the composite
        // expansion has a single authority.
        if (ps[0] == 0x0E)
        {
            if (count == 2 && ps[1] == 0x1D)
                return I(newline ? "il2cpp::runtime::icall_console_write_line_fmt_array" : "il2cpp::runtime::icall_console_write_fmt_array", 2, false, false, false, new[] { "", "a" });
            if (count == 2 && ps[1] is 0x1C or 0x12)
                return I(newline ? "il2cpp::runtime::icall_console_write_line_fmt1" : "il2cpp::runtime::icall_console_write_fmt1", 2, false, false, false, new[] { "", "" });
            if (count == 3 && ps[1] is 0x1C or 0x12 && ps[2] is 0x1C or 0x12)
                return I(newline ? "il2cpp::runtime::icall_console_write_line_fmt2" : "il2cpp::runtime::icall_console_write_fmt2", 3, false, false, false, new[] { "", "", "" });
            if (count == 4 && ps[1] is 0x1C or 0x12 && ps[2] is 0x1C or 0x12 && ps[3] is 0x1C or 0x12)
                return I(newline ? "il2cpp::runtime::icall_console_write_line_fmt3" : "il2cpp::runtime::icall_console_write_fmt3", 4, false, false, false, new[] { "", "", "", "" });
        }
        // char[] slice form: Write(char[] buffer, int index, int count).
        if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
            return I(newline ? "il2cpp::runtime::icall_console_write_line_chars_range" : "il2cpp::runtime::icall_console_write_chars_range", 3, false, false, false, new[] { "a", "i", "i" });
        throw new NotSupportedException(
            $"Console.{(newline ? "WriteLine" : "Write")} with {count} param(s) (0x{ps[0]:X2}, ...) not wired in M5");
    }

    // Console STREAM TextWriter members -- the Write/WriteLine surface of the
    // console stream objects (Console.Out / Console.Error / TextWriter.Null,
    // console.cpp icall_console_stream_write[_line]). The receiver (the stream
    // object) passes through as arg[0] with the "inst" shape; the explicit
    // text arg passes as-is (Il2CppObject*; the runtime encodes UTF-8
    // internally). A null text is a no-op (net48 TextWriter.Write/WriteLine
    // (null)); WriteLine() writes just the terminator.
    //
    // Console.In (the "System.Console.InReader" pseudo-type) shares this block:
    // its Read/Peek/ReadLine/ReadToEnd members route to the stdin icalls (the
    // receiver is the reader singletons -- the runtime ignores it).
    private static ResolvedMethod? TryMapConsoleStream(string owner, string name, byte[] ps, int count,
        string? receiverType = null)
    {
        if (receiverType == "System.Console.InReader")
        {
            switch (name)
            {
                case "Read":
                    return count == 0
                        ? I("il2cpp::runtime::icall_console_in_read", 0, true, false, true, new[] { "drop" })
                        : null;
                case "Peek":
                    return count == 0
                        ? I("il2cpp::runtime::icall_console_in_peek", 0, true, false, true, new[] { "drop" })
                        : null;
                case "ReadLine":
                    return count == 0
                        ? I("il2cpp::runtime::icall_console_in_read_line", 0, true, true, true, new[] { "drop" },
                            returnTypeName: "System.String")
                        : null;
                case "ReadToEnd":
                    return count == 0
                        ? I("il2cpp::runtime::icall_console_in_read_to_end", 0, true, true, true, new[] { "drop" },
                            returnTypeName: "System.String")
                        : null;
                case "Close":
                case "Dispose":
                    return count == 0
                        ? I("il2cpp::runtime::icall_console_stream_write", 0, false, false, true, new[] { "drop" })
                        : null;
                default:
                    throw new NotSupportedException(
                        $"Console.In TextReader member '{name}' not wired (Read/Peek/ReadLine/ReadToEnd only)");
            }
        }
        // TextWriter.StringBuilder-shaped stream members: Flush is a no-op
        // (stdio is unbuffered for our purposes) but must still resolve.
        if (name == "Flush" && count == 0)
            return I("il2cpp::runtime::icall_console_stream_write", 0, false, false, true, new[] { "drop" });
        if (name == "get_NewLine")
            return I("il2cpp::runtime::icall_environment_get_newline", 0, true, true, true, new[] { "drop" },
                returnTypeName: "System.String");
        if (name == "Write")
        {
            switch (ps.Length == 0 ? (byte)0 : ps[0])
            {
                case 0x0E when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write", 1, false, false, true);
                case 0x08 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x02 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x03 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x0A when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x0D when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x1C or 0x12 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_object", 1, false, false, true, new[] { "", "" });
                case 0x1D when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_chars", 1, false, false, true, new[] { "", "a" });
            }
            throw new NotSupportedException(
                $"console stream TextWriter.Write overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired");
        }
        if (name == "WriteLine")
        {
            switch (ps.Length == 0 ? (byte)0 : ps[0])
            {
                case 0x00 when count == 0:
                    return I("il2cpp::runtime::icall_console_stream_write_line", 0, false, false, true);
                case 0x0E when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line", 1, false, false, true);
                case 0x08 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x02 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x03 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x0A when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x0D when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_object", 1, false, false, true, new[] { "", "box:i" });
                case 0x1C or 0x12 when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_object", 1, false, false, true, new[] { "", "" });
                case 0x1D when count == 1:
                    return I("il2cpp::runtime::icall_console_stream_write_line_chars", 1, false, false, true, new[] { "", "a" });
            }
            throw new NotSupportedException(
                $"console stream TextWriter.WriteLine overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired");
        }
        throw new NotSupportedException($"console stream TextWriter member '{name}' not wired (Write/WriteLine/Flush)");
    }
}
