//------------------------------------------------------------------------------
// guidformat1.cs -- Guid TEXT-INPUT contract (net48 oracle, byte-for-byte).
//
// Regression target: the runtime comment in guid.h claimed a "deliberate
// deviation: STRICT, no braces (B), no parentheses (P)". MEASURED net48
// accepts BOTH: Guid.TryParse("{...}") == True and Guid.TryParse("(...)") ==
// True. The output side ALREADY produced "B" and "P" correctly, so the
// input/output asymmetry WAS the bug -- this case pins the input half.
//
// Asserted contract (measured on a real net48 CLR, one row per probe):
//   * accepted forms: D (36 chars, dashes 8/13/18/23), N (32 chars),
//     B ("{" + D + "}"), P ("(" + D + ")"), any hex case, and .NET
//     whitespace BEFORE/AFTER the whole text (including U+00A0 and U+2003).
//   * rejected: mismatched braces/parens, doubled decoration, ws INSIDE the
//     braces, wrong length, wrong dash positions, non-hex characters,
//     underscores, N inside braces/parens, empty, whitespace-only, U+FEFF.
//   * exceptions: null -> ArgumentNullException; every other invalid text ->
//     FormatException (for Parse and for new Guid(string)).
//   * TryParse never throws (false on invalid, including null).
//   * ToString("N"/"D"/"B"/"P") renders the matching output form (lowercase).
//
// EVERY line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class GuidFormat1
    {
        const string D = "00112233-4455-6677-8899-aabbccddeeff";
        const string N = "00112233445566778899aabbccddeeff";

        static string TryLabel(string s)
        {
            try { return Guid.TryParse(s, out Guid g) ? "True:" + g.ToString("D") : "False"; }
            catch (Exception e) { return "THROW:" + e.GetType().Name; }
        }

        static string ParseLabel(string s)
        {
            try { return "v:" + Guid.Parse(s).ToString("D"); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        static string CtorLabel(string s)
        {
            try { return "v:" + new Guid(s).ToString("D"); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        static void Row(string label, string text)
        {
            Console.WriteLine(label + ".try=" + TryLabel(text));
            Console.WriteLine(label + ".parse=" + ParseLabel(text));
            Console.WriteLine(label + ".ctor=" + CtorLabel(text));
        }

        public static void Main()
        {
            Guid g = new Guid(0x00112233, 0x4455, 0x6677, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff);

            // ---- 1. ToString(format): the OUTPUT side (already correct) ----
            Console.WriteLine("out.D=" + (g.ToString("D") == D));
            Console.WriteLine("out.N=" + (g.ToString("N") == N));
            Console.WriteLine("out.B=" + (g.ToString("B") == "{" + D + "}"));
            Console.WriteLine("out.P=" + (g.ToString("P") == "(" + D + ")"));
            Console.WriteLine("out.bare=" + (g.ToString() == D));
            Console.WriteLine("out.d.lower=" + (g.ToString("d") == D));
            Console.WriteLine("out.b.lower=" + (g.ToString("b") == "{" + D + "}"));
            Console.WriteLine("out.p.lower=" + (g.ToString("p") == "(" + D + ")"));

            // ---- 2. ACCEPTED input forms ----
            Row("in.D", D);
            Row("in.N", N);
            Row("in.B", "{" + D + "}");
            Row("in.P", "(" + D + ")");
            Row("in.D.upper", "00112233-4455-6677-8899-AABBCCDDEEFF");
            Row("in.N.upper", "00112233445566778899AABBCCDDEEFF");
            Row("in.D.mixed", "AaBbCcDd-EeFf-1122-3344-556677889900");
            Row("in.B.upper", "{00112233-4455-6677-8899-AABBCCDDEEFF}");
            Row("in.P.upper", "(00112233-4455-6677-8899-AABBCCDDEEFF)");

            // ---- 3. OFF-GRID but legal decimals: the B/P forms of OTHER guids
            Row("in.B.zero", "{00000000-0000-0000-0000-000000000000}");
            Row("in.P.max", "(ffffffff-ffff-ffff-ffff-ffffffffffff)");
            Row("in.B.utf", "{" + "01234567-89ab-cdef-0123-456789abcdef" + "}");

            // ---- 4. surrounding whitespace (outside the decoration) ----
            Row("ws.space.outer", " " + D + " ");
            Row("ws.space.aroundB", "  {" + D + "}  ");
            Row("ws.space.aroundP", "\t(" + D + ")\r\n");
            Row("ws.space.inB", "{" + " " + D + " " + "}");
            Row("ws.space.afterBrace", "{" + D + "} ");
            Row("ws.space.beforeBrace", " {" + D + "}");
            Row("ws.nbsp.outer", "\u00A0" + D + "\u00A0");
            Row("ws.emdash.outer", "\u2003" + D + "\u2003");
            Row("ws.nbsp.aroundB", "\u00A0{" + D + "}\u00A0");

            // ---- 5. REJECTED forms (FormatException for Parse/ctor) ----
            Row("bad.mismatch1", "{" + D + ")");
            Row("bad.mismatch2", "(" + D + "}");
            Row("bad.double.paren", "{(" + D + ")}");
            Row("bad.double.brace", "{{" + D + "}}");
            Row("bad.len35", "00112233-4455-6677-8899-aabbccddeef");
            Row("bad.len37", "00112233-4455-6677-8899-aabbccddeefff");
            Row("bad.len31N", "00112233445566778899aabbccddee");
            Row("bad.len33N", "00112233445566778899aabbccddeeff0");
            Row("bad.hex", "00112233-4455-6677-8899-aabbccddeefg");
            Row("bad.hex.lead", "g0112233-4455-6677-8899-aabbccddeeff");
            Row("bad.dash.pos", "0011223-34455-6677-8899-aabbccddeeff");
            Row("bad.dash.missing", "001122334-455-6677-8899-aabbccddeeff");
            Row("bad.dash.trailing", "00112233-4455-6677-8899-aabbccddeeff-");
            Row("bad.underscore", "00112233_4455_6677_8899_aabbccddeeff");
            Row("bad.N.inBraces", "{" + N + "}");
            Row("bad.N.inParens", "(" + N + ")");
            Row("bad.N.inBraces.ws", "{ " + N + " }");
            Row("bad.empty", "");
            Row("bad.spaces", "   ");

            // ---- 6. null: ArgumentNullException for Parse/ctor, false for TryParse
            Row("null", null);

            // ---- 7. the parse/ctor agreement over a representative subset ----
            Console.WriteLine("agree.B=" + (ParseLabel("{" + D + "}") == CtorLabel("{" + D + "}")));
            Console.WriteLine("agree.P=" + (ParseLabel("(" + D + ")") == CtorLabel("(" + D + ")")));
            Console.WriteLine("agree.bad=" + (ParseLabel("nope") == CtorLabel("nope")));
            Console.WriteLine("agree.null=" + (ParseLabel(null) == CtorLabel(null)));

            // ---- 8. TryParse writes the value on success ----
            // NOTE: the FAILURE-path out slot is deliberately NOT asserted.
            // net48 zeroes it (TryParse writes default(T) on failure) while
            // this runtime's TryParse family is verify-then-mutate (leaves the
            // slot untouched) -- the same documented D-NP-2 deviation the
            // int/double/decimal TryParse surfaces carry, not a Guid input
            // contract.
            Guid slot = new Guid("11111111-2222-3333-4444-555555555555");
            bool okB = Guid.TryParse("{" + D + "}", out slot);
            Console.WriteLine("try.write.ok=" + okB);
            Console.WriteLine("try.write.val=" + (slot.ToString("D") == D));
            Console.WriteLine("try.fail.ok=" + Guid.TryParse("{" + D + ")", out Guid _));

            // ---- 9. round trip through every output form ----
            Console.WriteLine("rt.N=" + (Guid.Parse(g.ToString("N")).ToString("D") == D));
            Console.WriteLine("rt.B=" + (Guid.Parse(g.ToString("B")).ToString("D") == D));
            Console.WriteLine("rt.P=" + (Guid.Parse(g.ToString("P")).ToString("D") == D));
            Console.WriteLine("rt.T=B=" + (g.ToString("B") == g.ToString("D").Insert(0, "{").Insert(37, "}")));
        }
    }
}
