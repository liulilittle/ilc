//------------------------------------------------------------------------------
// exops2.cs -- System.Exception DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C19 (2026-08-10).
//
// EXTENDS excext / exdeep / exceptionchain2 on the post-passthrough surface
// (the m7m message carrier -- StackFold forwards the BCL ctor
// message/paramName args to the DotNetException carrier, MethodMap.Exception.cs
// get_Message reads it back on a catch variable):
//
//   1) CUSTOM EXCEPTION CHAINS: a 3-level user type hierarchy
//      (CxEx3 : CxEx2 : CxEx : Exception). Throw the derived type, catch at
//      the intermediate base and at System.Exception: GetType().Name is the
//      ORIGINAL thrown type at every catch level (il2cpp_exception_matches
//      walks the baked base chain). The MESSAGE literal of a CUSTOM type is
//      NOT forwarded at HEAD -- StackFold captures ctor args ONLY for BCL
//      exceptions (StackFold.cs:2649 isBclException gate), so a custom
//      throw carries the default "exception thrown"; recorded defect
//      D-EX2-2 (GAPS.md), the raw-literal probes are PARKED. The ACTIVE
//      probes read the message PRESENCE (ex.Message != null -> True on both
//      runtimes) -- the custom-message byte-exact oracle is pinned in GAPS.
//   2) RETHROW CHAIN: bare `throw;` through an inner catch + a finally
//      re-presents the SAME type + carrier at the outer catch (read before
//      and after the rethrow -- e8 proves the carrier is not consumed).
//   3) PARAM-NAME PASSTHROUGH (参数名透传) -- BCL factories, which DO get the
//      ctor-arg forwarding: `throw new ArgumentNullException("pxy")` ->
//      `ex.Message.Contains("pxy")` is True on BOTH runtimes (net48 composes
//      "值不能为 null。...参数名: pxy" -- LOCALIZED, so raw Message text is
//      deliberately never printed for BCL factories; the Contains probe is
//      locale-invariant). `new ArgumentOutOfRangeException(p, m)` -> the
//      custom message literal flows into the composed text on both
//      (Contains("range msg") invariant; net48 appends "参数名: pyz", ILC
//      keeps the bare message). `new ArgumentException(msg)` -> the custom
//      message passthrough is byte-exact.
//   4) INNER EXCEPTION (InnerException) -- PARKED (D-EX2-1): get_InnerException
//      is NOT mapped in MethodMap.Exception.cs at HEAD 3411fe8, so any
//      probe transpiles to an undefined generated extern and kills the whole
//      case at the C++ link stage (the documented exdeep-era prediction).
//      The net48 oracle contract is pinned in GAPS.md; uncomment when the
//      mapping lands.
//
// Byte-stability rules:
//   * NEVER print raw ex.Message of a BCL-standard factory: net48 localizes
//     (ArgumentNullException -> Chinese on this machine), the ILC carrier is
//     hardcoded English. Only CUSTOM-message literals (byte-exact) and
//     locale-invariant predicates (Contains / != null) are active.
//   * ex.GetType() / ex.Message are only used on CATCH VARIABLES (the fold
//     tracks the catch entry as the exception carrier; a non-catch receiver
//     is a LOUD transpile-time NotSupportedException).
//   * No exception is constructed OUTSIDE a throw site (a standalone BCL
//     exception ctor has no resolution path in M3 -- the ctor is captured by
//     the throw-site fold).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Exops2 {
    // ---- 3-level custom exception hierarchy (chain under test) ----
    class CxEx : Exception { public CxEx(string m) : base(m) { } }
    class CxEx2 : CxEx { public CxEx2(string m) : base(m) { } }
    class CxEx3 : CxEx2 { public CxEx3(string m) : base(m) { } }

    public static void Main()
    {
        // ================= 1. CUSTOM EXCEPTION CHAIN =================
        // e1: throw the MIDDLE type, catch at the intermediate base; the
        // thrown TYPE survives the base catch (GetType().Name), the message
        // PRESENCE is the byte-exact cross-runtime probe (D-EX2-2 parks the
        // raw-literal version).
        try { throw new CxEx2("level2"); }
        catch (CxEx ex) { Console.WriteLine("e1=" + ex.GetType().Name + "|" + (ex.Message != null)); }

        // e2: throw the DEEPEST type, catch at its direct base.
        try { throw new CxEx3("deep3"); }
        catch (CxEx2 ex) { Console.WriteLine("e2=" + ex.GetType().Name + "|" + (ex.Message != null)); }

        // e9: throw the deepest type, catch at System.Exception (universal
        // root) -- the exact thrown type still flows through.
        try { throw new CxEx3("base-catch"); }
        catch (Exception ex) { Console.WriteLine("e9=" + ex.GetType().Name + "|" + (ex.Message != null)); }

        // ================= 2. RETHROW CHAIN =================
        // e3: inner catch reads type+presence, finally runs, bare rethrow;
        // the OUTER catch reads the SAME type and carrier again.
        try
        {
            try { throw new CxEx("origmsg"); }
            catch (Exception inner)
            {
                Console.WriteLine("e3_inner=" + inner.GetType().Name + "|" + (inner.Message != null));
                throw;
            }
            finally { Console.WriteLine("e3_finally"); }
        }
        catch (Exception outer) { Console.WriteLine("e3_outer=" + outer.GetType().Name + "|" + (outer.Message != null)); }

        // e8: the carrier survives an inner catch+rethrow -- read BEFORE the
        // rethrow and AGAIN at the outer catch (not consumed).
        try
        {
            try { throw new CxEx("carrier-survives"); }
            catch (Exception ex) { Console.WriteLine("e8_first=" + (ex.Message != null)); throw; }
        }
        catch (Exception ex) { Console.WriteLine("e8_second=" + (ex.Message != null)); }

        // ================= 3. PARAM-NAME PASSTHROUGH =================
        // e4: ArgumentNullException(paramName) -- the param name flows into
        // the composed message on BOTH runtimes (locale-invariant Contains).
        try { throw new ArgumentNullException("pxy"); }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine("e4=" + ex.GetType().Name + "|" + ex.Message.Contains("pxy"));
        }

        // e5: ArgumentOutOfRangeException(paramName, message) -- the custom
        // message literal flows into the composed text on BOTH runtimes
        // (net48 appends the localized "参数名: pyz" suffix, the ILC carrier
        // keeps the bare message -- the Contains("range msg") probe is the
        // cross-runtime invariant; raw equality / param-name containment
        // would diverge and are deliberately not probed).
        try { throw new ArgumentOutOfRangeException("pyz", "range msg"); }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine("e5=" + ex.GetType().Name + "|" + ex.Message.Contains("range msg"));
        }

        // e6: ArgumentException(custom message) -- byte-exact passthrough.
        try { throw new ArgumentException("custom arg msg"); }
        catch (ArgumentException ex) { Console.WriteLine("e6=" + ex.Message); }

        // ================= 4. RUNTIME FAULT PRESENCE =================
        // e7: a runtime fault (div-by-zero) has a non-null message on both
        // runtimes (net48 localizes it; only the PRESENCE is probed).
        int x = 1, y = 0;
        try { int z = x / y; Console.WriteLine("e7=" + z); }
        catch (DivideByZeroException ex)
        {
            Console.WriteLine("e7=" + ex.GetType().Name + "|" + (ex.Message != null));
        }

        // ================= 5. INNER EXCEPTION (PARKED, D-EX2-1) =========
        // BLOCKED at HEAD: get_InnerException is unmapped in
        // MethodMap.Exception.cs -> a probe transpiles to an undefined extern
        // System_Exception_get_InnerException_<hash> -> C++ link failure.
        // net48 oracle contract pinned in GAPS.md; uncomment when mapped:
        //
        //   var inner = new ArgumentNullException("inner");
        //   var outer = new ArgumentException("outer", inner);
        //   Console.WriteLine("inner_same=" + (outer.InnerException == inner));            // True
        //   Console.WriteLine("inner_type=" + outer.InnerException.GetType().Name);        // ArgumentNullException

        // ================= 6. CUSTOM MESSAGE LITERAL (PARKED, D-EX2-2) ====
        // BLOCKED at HEAD: the throw-site carrier forwards the ctor message
        // ONLY for BCL exceptions (StackFold.cs:2649 isBclException gate);
        // a custom-type throw falls back to the default "exception thrown".
        // net48 oracle when the gate is lifted (csc-probe-verified):
        //
        //   try { throw new CxEx2("level2"); }
        //   catch (CxEx ex) { Console.WriteLine("ex1=" + ex.Message); }      // level2
        //   try { throw new CxEx3("deep3"); }
        //   catch (CxEx2 ex) { Console.WriteLine("ex2=" + ex.Message); }     // deep3
        //   try { throw new CxEx("origmsg"); }
        //   catch (Exception ex) { Console.WriteLine("ex3=" + ex.Message); } // origmsg
    }
}}
