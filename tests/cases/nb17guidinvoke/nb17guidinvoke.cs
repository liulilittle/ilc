// nb17guidinvoke.cs -- Guid value produced by a delegate Invoke used directly
// as an operand of Guid equality. IsGuidOperand (CodeGenerator.cs:10025)
// recognizes LocExpr/LocAddrExpr/FieldExpr/FieldAddrExpr/StaticFieldExpr but
// NOT a DelegateInvokeExpr, so the Guid icall receives the struct VALUE
// instead of its address.
using System;

namespace Cases
{
    public static class Nb17GuidInvoke
    {
        private static Guid Echo(Guid g) { return g; }

        public static void Main()
        {
            Guid one = Guid.Parse("00000000-0000-0000-0000-000000000001");
            Func<Guid, Guid> f = Echo;
            Console.WriteLine("j_eq=" + (f(one) == one));
            Guid viaLoc = f(one);
            Console.WriteLine("j_loc=" + (viaLoc == one));
        }
    }
}