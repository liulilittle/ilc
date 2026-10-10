// p-gn14nestedcap.cs -- GN-14 (a-2) probe: a NESTED type's instance field whose
// type is GENERICINST Dictionary`2<VAR 0, System.String> (the nested type lives
// inside a generic type and captures the outer type parameter). The field
// signature is decoded by TypeEmitter.DecodeNestedFieldSignature ->
// NestedTypeArgName.
//
// HEAD defect: NestedTypeArgName's 0x13/0x1E arm returned "int" WITHOUT
// consuming the compressed VAR index, so the next argument byte (the index
// 0x00) was misread through the tolerant fallback as "System.Object". The
// generated typeinfo field name forked to
//   "System.Collections.Generic.Dictionary`2<int,System.Object>"
// instead of
//   "System.Collections.Generic.Dictionary`2<int,System.String>".
// The fix consumes the index (mirroring MethodBodyReader.ConsumeGenericParamSuffix)
// and keeps the call-side default "int".
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases {

class Outer<T> {
    public class Inner {
        public Dictionary<T, string> d;
    }
    public static int Probe() { return 7; }
}

public static class PGn14NestedCap {
    public static void Main() {
        Console.WriteLine(Outer<int>.Probe());
    }
}}