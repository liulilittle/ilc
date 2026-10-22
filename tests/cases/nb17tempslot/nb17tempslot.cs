// nb17tempslot.cs -- a Guid-returning delegate Invoke whose result lands on a
// COMPILER TEMP (branch merge) instead of an IL-declared local.
// CodeGenerator.cs:2509-2517 (the DelegateInvokeExpr compensation branch) has
// no arm for a struct return, so the temp fell to "val" -> int32_t while the
// helper returns il2cpp::Il2CppGuid.
using System;

namespace Cases
{
    public static class Nb17TempSlot
    {
        private static Guid Echo(Guid g) { return g; }

        public static void Main()
        {
            Guid one = Guid.Parse("00000000-0000-0000-0000-000000000001");
            bool pick = true;
            Func<Guid, Guid> f = Echo;
            Console.WriteLine("f_ternary=" + (pick ? f(one) : f(one)).ToString());
        }
    }
}