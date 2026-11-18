using System;
using System.Runtime.InteropServices;

// LOUD contract: the DELEGATE half of the phantom guard.
//
// A phantom BCL resolution is worse when its name is used as a FUNCTION
// POINTER than when it is called. The call arms (EmitStmt / EmitTask9Call)
// throw at transpile time, but a delegate bound to the same unmapped method
// reaches CodeGenerator.MethodCppNameFromToken, which emitted
// &System_Runtime_InteropServices_Marshal_GetExceptionForHR_<hash>. Nothing
// defines that symbol, yet the transpile still EXITED 0 -- the failure only
// appeared at cmake, as "use of undeclared identifier".
//
// Both halves must reject. This case pins the delegate half; loud-marshal-hr
// pins the call half. They are separate cases because they take different
// emitter paths and a fix to one does not cover the other.
public static class P
{
    public static void Main()
    {
        // ldftn + delegate construction: the method address is taken, not called.
        Func<int, Exception> f = Marshal.GetExceptionForHR;
        Console.WriteLine("[d] made=" + (f != null));
    }
}
