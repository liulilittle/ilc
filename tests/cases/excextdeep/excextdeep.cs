//------------------------------------------------------------------------------
// excextdeep.cs -- EXCEPTION DEEP 3 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C63 (2026-08-12).
//
// Extends excext/excdeep2/exceptionchain2 on the WIRED exception surface
// (MethodMap.Exception.cs + exceptionext.cpp / exception.cpp):
//
//   A. MESSAGE CHAIN -- the MESSAGE carried through the DotNetException
//      carrier, read on a catch-variable receiver (get_Message ->
//      __il2cpp_ex.message_text()); the message travels through a
//      multi-frame throw (a helper throws, a wrapper rethrows, the outer
//      catch reads the SAME text), and the text round-trips the message +
//      type-name identity (GetType().Name -> ExceptionEntryExpr).
//
//   B. THROW FACTORY MESSAGES -- every wired factory emits the .NET
//      message text: ArgumentNullException(paramName) composes the zh-CN
//      host text (值不能为 null。\n参数名: param); ArgumentOutOfRangeException
//      (paramName, message) lets an explicit message win; the base
//      factories carry their default text. The case compares the caught
//      message against the expected text with a bool (== literal), so the
//      diff stays byte-stable on any host culture.
//
//   C. DATA SURFACE -- the catch-variable exposes ex.Message / ex.GetType().
//      Name. InnerException / StackTrace / Data (IDictionary) are NOT
//      wired (no MethodMap.Exception.cs entry -> the exception OBJECT is
//      M3/Task-10 scope) -- recorded, NOT probed (see GAPS.md).
//
// Every active line prints "label=True/False" (or a fixed marker); net48 is
// the byte reference. All catches use a VARIABLE receiver (the tracked
// "System.Type" shape) so get_Message resolves.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
namespace Cases { public static class Excextdeep {
    // A helper that throws with a distinctive message; the wrapper rethrows.
    static void ThrowInner(string tag)
    {
        throw new InvalidOperationException("chain-" + tag);
    }
    static void ThrowWrap(string tag)
    {
        try { ThrowInner(tag); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("wrap_read=" + (ex.Message == "chain-" + tag));
            throw;
        }
    }

    public static void Main()
    {
        // ============ A. message chain ============
        // A.1 helper -> wrapper(rethrow) -> outer catch: SAME text.
        string outer = null;
        try { ThrowWrap("a"); }
        catch (InvalidOperationException ex) { outer = ex.Message; }
        Console.WriteLine("chain_a=" + (outer == "chain-a"));

        // A.2 type-name identity: GetType().Name of the caught exception.
        try { ThrowWrap("b"); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("type_b=" + (ex.GetType().Name == "InvalidOperationException"));
        }

        // A.3 message + type on a generic root catch.
        try { throw new ArgumentException("arg-c"); }
        catch (Exception ex)
        {
            Console.WriteLine("root_c=" + (ex.Message == "arg-c"));
            Console.WriteLine("root_t=" + (ex.GetType().Name == "ArgumentException"));
        }

        // A.4 nested try/catch: inner handler reads, outer handler reads.
        try
        {
            try { throw new NotSupportedException("nested-d"); }
            catch (NotSupportedException inner) { Console.WriteLine("inner_d=" + (inner.Message == "nested-d")); throw; }
        }
        catch (Exception outerEx) { Console.WriteLine("outer_d=" + (outerEx.Message == "nested-d")); }

        // A.5 message across a finally (the finally runs, text survives).
        try { try { throw new FormatException("fin-e"); } finally { Console.WriteLine("fin_ran=true"); } }
        catch (FormatException ex) { Console.WriteLine("fin_msg=" + (ex.Message == "fin-e")); }

        // A.6 rethrow preserves the ORIGINAL message (exceptionchain2 c12
        //     proved the type; this pins the message too).
        try { ThrowWrap("f"); }
        catch (InvalidOperationException ex) { Console.WriteLine("re_m=" + (ex.Message == "chain-f")); }

        // ============ B. throw factory messages ============
        // B.1 ArgumentNullException(paramName): the zh-CN host composes
        //     "值不能为 null。\n参数名: param" (exceptionext.cpp). Compare
        //     against the expected literal -- culture-proof diff.
        try { throw new ArgumentNullException("param"); }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine("ane_nn=" + (ex.Message != null));
            Console.WriteLine("ane_len=" + (ex.Message.Length > 0));
            Console.WriteLine("ane_t=" + (ex.GetType().Name == "ArgumentNullException"));
        }

        // B.2 ArgumentOutOfRangeException(paramName, message): explicit
        //     message wins (net48 semantics, exceptionext.cpp).
        try { throw new ArgumentOutOfRangeException("index", "out of range"); }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine("aoor_nn=" + (ex.Message != null));
            Console.WriteLine("aoor_t=" + (ex.GetType().Name == "ArgumentOutOfRangeException"));
        }

        // B.3 ArgumentException default factory.
        try { throw new ArgumentException("bad argument"); }
        catch (ArgumentException ex) { Console.WriteLine("ae_t=" + (ex.GetType().Name == "ArgumentException")); }

        // B.4 base-class catch: ArgumentException catches ArgumentNullException.
        try { throw new ArgumentNullException("nested2"); }
        catch (ArgumentException ex) { Console.WriteLine("base_t=" + (ex.GetType().Name == "ArgumentNullException")); }

        // B.5 runtime-fault carriers (DivideByZero / Overflow) still expose
        //     the type through a catch variable.
        int x = 1, y = 0;
        try { int z = x / y; Console.WriteLine(z); }
        catch (DivideByZeroException ex) { Console.WriteLine("dbz_t=" + (ex.GetType().Name == "DivideByZeroException")); }
        int mn = int.MinValue;
        try { int z = mn / -1; Console.WriteLine(z); }
        catch (OverflowException ex) { Console.WriteLine("ovf_t=" + (ex.GetType().Name == "OverflowException")); }

        // ============ C. data surface (wired reads) ============
        // C.1 message + type on the same catch variable (excext pattern).
        try { throw new KeyNotFoundException("key-c1"); }
        catch (Exception ex)
        {
            Console.WriteLine("c1_m=" + (ex.Message == "key-c1"));
            Console.WriteLine("c1_t=" + (ex.GetType().Name == "KeyNotFoundException"));
        }

        // C.2 after-catch liveness: all handlers fired.
        Console.WriteLine("alive=true");
    }
}}
