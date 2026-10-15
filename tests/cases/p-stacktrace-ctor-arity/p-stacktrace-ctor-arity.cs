using System;
using System.Diagnostics;

// reg-stacktrace-ctor-arity.cs -- D2-r audit probe: StackTrace ctors with
// NON-ZERO arity. The D2 fix wired only the 0-arity path (`new StackTrace()`)
// to the single-param stub icall_stacktrace_ctor(Il2CppObject*). For
// arity>0 the `.ctor` arm returned I(..., count, ...) with NO drop cast, so
// the generated call passed the explicit args -> cmake FAIL "too many
// arguments". net48 ctor surface covered here (all construction-only, no
// frame read = byte-exact neutral):
//   new StackTrace(bool fNeedFileInfo)
//   new StackTrace(int skipFrames, bool fNeedFileInfo)
//   new StackTrace(int skipFrames)
//   new StackTrace(Exception e) -- with a non-null argument (net48 throws on
//   null: ArgumentNullException paramName="exception").
namespace Cases
{
    public static class RegStackTraceCtorArity
    {
        public static void Main()
        {
            var a = new StackTrace(true);
            var b = new StackTrace(0, false);
            var c = new StackTrace(0);
            var ex = new Exception("probe");
            var d = new StackTrace(ex);
            if (a == null || b == null || c == null || d == null)
                throw new Exception("unreachable");
            Console.WriteLine("ctor-arity ok");
        }
    }
}