//------------------------------------------------------------------------------
// g4dicturistr.cs -- T4 class-NOT-last generic-argument probe (differential).
//
// PURPOSE (T4-CLASSELEM-FIDELITY): Dictionary`2<Uri,string> is the canonical
// CLASS-argument-NOT-LAST TypeSpec. Per ECMA-335 II.23.1.16 the arg encoding
// MEASURED (2026-09-19, #Blob walk of this exe, blob@42) is
//   15 12 39 02 12 35 0e
//   15 GENERICINST | 12 CLASS(marker) | 39 <DictDef coded index> | 02 argc |
//   12 CLASS(marker) | 35 <arg0 handle = TypeRef row 13 = System.Uri> | 0e STRING
// NOTE: the DEF handle (0x39) and the ARG0 handle (0x35) are both TypeRef
// coded indices; SPEC §2 illustrated arg0 as 0x39, the real value here is 0x35.
// Before the T4 fix the 0x12 arg arm returned "class" WITHOUT consuming 0x35,
// so arg1's ReadByte read 0x35 as a type byte.
//
// BASELINE (dll 3D0B30F3, pre-T4) -- MEASURED, differs from SPEC §2's
// "hard transpile failure" prediction: the fold is a SOFT skip, not an abort.
//   * transpile log: `warning: method fold failed -> skipped
//     (Cases.Prog::Main: generic argument byte 0x35 in M3)` (rc=0);
//   * a NotTranspiled stub is emitted, so the runtime exe builds but exits 1:
//     `Unhandled managed exception: this method was not transpiled (M5 mapping
//     gap): Cases.Prog::Main`.
// The observe-FAIL anchor is therefore: transpile log contains
// `generic argument byte 0x35` AND runtime rc=1 (not a difftest transpile throw).
// POST-T4: the 0x12 arm consumes the handle, so the byte-decode warning
// disappears; the remaining state is either a Dictionary-mapping LOUD naming
// the decoded instance (System.Collections.Generic.Dictionary`2_System.Uri_string)
// or transpile+run if the Uri-key domain is wired.
//
// Dictionary`2<Uri,string> is NOT a wired AOT expansion (only string->int32
// and string->string exist), so a mapping-arm LOUD is the expected post-fix
// state until the Uri-key domain is added. The probe's job is the BYTE-DECODE
// fix, not Dictionary wiring.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases
{
    public static class Prog
    {
        public static void Main()
        {
            Console.WriteLine("-- g4dicturistr start --");
            Uri k1 = new Uri("http://example.com/a");
            Uri k2 = new Uri("https://host/b");
            Dictionary<Uri, string> d = new Dictionary<Uri, string>();
            d.Add(k1, "one");
            d.Add(k2, "two");
            Console.WriteLine("dcount=" + d.Count);
            Console.WriteLine("dget=" + d[k1]);
            Console.WriteLine("dhas=" + d.ContainsKey(k2));
            Console.WriteLine("-- g4dicturistr end --");
        }
    }
}
