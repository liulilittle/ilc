//------------------------------------------------------------------------------
// p-nb-exc.cs -- batch nb1 EXC-CHAIN difftest probe (net48 oracle ==
// transpiled runtime byte-exact).
//
// Reproduces the exploration RED (p-nextbatch 31/32: [exc.list.msg]
// expected=outer actual=) and the chain surface:
//   1. .ctor(string)        -- message captured (already wired).
//   2. .ctor(string, Exception) -- message + inner chain captured (the RED
//      fix: the exploration build showed exc.list.msg empty; the 2-arg ctor
//      silently lost the message).
//   3. get_Message via a List<Exception> element (the ilistException shape).
//   4. get_InnerException -> the inner object.
//   5. GetBaseException() -> the root of the inner chain (returns the
//      receiver itself when there is no inner -- [exc.base.solo]=True).
//   6. get_Source / get_HelpLink / get_TargetSite -- net48 returns null for
//      unset values (constant-null arms; net48 oracle pins all null).
//
// The oracle evidence (oracle_out.txt:21-32 / nexc_oracle.txt:1-9):
//   inner.kind = the fallback category: an inner WITHOUT its own captured
//   message must still be a usable Exception receiver (GetBaseException
//   walks the object chain, not the message chain).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases
{
    public static class PNbExc
    {
        public static void Main()
        {
            // 1. 2-arg ctor: message + inner captured.
            var inner = new Exception("inner");
            var outer = new Exception("outer", inner);
            Console.WriteLine("[exc.msg]=" + outer.Message);
            // The exploration RED: a List<Exception> element read-back.
            var list = new List<Exception>();
            list.Add(new Exception("outer", new Exception("inner")));
            Console.WriteLine("[exc.list.msg]=" + list[0].Message);
            // 2. Inner chain.
            Console.WriteLine("[exc.inner.msg]=" + outer.InnerException.Message);
            // 3. GetBaseException: root of the chain.
            Console.WriteLine("[exc.base.msg]=" + outer.GetBaseException().Message);
            // 4. GetBaseException on a SOLO exception (no inner) returns the
            //    receiver itself (net48 semantics; reference equality).
            var solo = new Exception("solo");
            Console.WriteLine("[exc.base.solo]=" + (solo.GetBaseException() == solo));
            // 5. Constant-null surface (net48 oracle pins null for unset).
            Console.WriteLine("[exc.src.null]=" + (outer.Source == null));
            Console.WriteLine("[exc.help.null]=" + (outer.HelpLink == null));
            Console.WriteLine("[exc.target.null]=" + (outer.TargetSite == null));
            // 6. get_StackTrace is null for a constructed, never-thrown
            //    exception (net48}). The carrier has no thrown state; the
            //    runtime reflects this via the constant-null arm.
            Console.WriteLine("[exc.stack.null]=" + (outer.StackTrace == null));
        }
    }
}