// fieldargcase -- minimal probe for the CodeGenerator FieldAccess value-type
// receiver forms (caudit2 fix): implicit this (T* arg0), BY-VALUE struct param
// (V:), and BY-REF struct param (B:V:). Top-level struct (no nested-type
// expansion needed). net48 oracle prints all True.
using System;

namespace Cases
{
    public struct FieldArgSt
    {
        public int V;
        public FieldArgSt(int v) { V = v; }
        public bool EqThis() { return this.V == V; }                 // this: T* arg0 -> `arg0->V`
        public bool EqVal(FieldArgSt o) { return V == o.V; }         // by-value struct param -> `(&(arg1))->V`
        public bool EqRef(ref FieldArgSt o) { return V == o.V; }     // by-ref struct param -> `arg1->V` (T*)
    }

    public static class FieldArg
    {
        public static void Main()
        {
            FieldArgSt a = new FieldArgSt(7);
            FieldArgSt b = new FieldArgSt(7);
            Console.WriteLine("this=" + a.EqThis() + " val=" + a.EqVal(b) + " ref=" + a.EqRef(ref b));
        }
    }
}
