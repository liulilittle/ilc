//------------------------------------------------------------------------------
// raudit1.cs -- REFLECTION AUDIT PROBE 1 (G6 static audit, suspicious #1):
// generic-closed typeof(T) name mapping.
//
// Probes the folded typeof(T) -> .Name pipeline for types whose ldtoken
// operand is a TypeSpecification (GenericInst) rather than a plain
// TypeDefOrRef:
//   * List<int>            -> net48 Name "List`1"
//   * int?                 -> net48 Name "Nullable`1"
//   * Dictionary<string,int> -> net48 Name "Dictionary`2"
//
// Static-audit finding (StackFold.cs): ldtoken only accepts TypeDefinition /
// TypeReference tokens; a TypeSpecification token has no fold and is expected
// to fail LOUD at transpile time (or, if folded, to produce a wrong name).
// This case pins the actual pipeline behaviour; net48 is the oracle baseline.
//
// Every line prints "label=<name>", byte-stable across console encodings.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace Cases { public static class Raudit1 {
    public static void Main()
    {
        Console.WriteLine("g_list=" + typeof(List<int>).Name);
        Console.WriteLine("g_nullable=" + typeof(int?).Name);
        Console.WriteLine("g_dict=" + typeof(Dictionary<string, int>).Name);
    }
}}
