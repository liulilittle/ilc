//------------------------------------------------------------------------------
// nestedenum1 -- enum ToString() on a TOP-LEVEL and a NESTED user enum.
//
// Theme (single): the constrained. callvirt Object::ToString() fold must find
// the enum's TypeDef by BOTH name forms. A nested enum's TypeDef.FullName is
// the SHORT name ("E") while the metadata token decodes to the '+' form
// ("Cases.NestedEnum1+E"); a comparison on FullName alone misses and the call
// falls to the generated-name phantom (il2cpp_enum_boxed_to_string on an
// int32_t* address -- uncompilable).
//
// Every line prints key=[value]; net48 prints the MEMBER NAME.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public enum TopE { A, B, C }

    public static class NestedEnum1
    {
        enum E { A, B, C }
        enum F { Zero, One, Two }

        static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

        public static void Main()
        {
            W("n1", E.C.ToString());
            W("n2", TopE.C.ToString());
            W("n3", E.A.ToString());
            W("n4", F.Two.ToString());
            W("n5", TopE.A.ToString());

            E e = E.B;
            W("n6", e.ToString());
            TopE t = TopE.B;
            W("n7", t.ToString());

            object o = E.C;
            W("n8", o.ToString());

            W("n9", (E)2 + "");
        }
    }
}
