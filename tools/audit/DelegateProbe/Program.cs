// Enumerates every TypeDef in an assembly whose direct base is System.MulticastDelegate,
// with its visibility and the shape of its .ctor signature. This is the ground truth
// for the delegate-ctor SIGNATURE test in StackFold.IsDelegateCtorSignature
// (checked by tools/audit/bcl_delegate_gate.py).
//
// The transpiler cannot do this itself: MetadataLoader loads only the user
// assembly, so mscorlib's and System.dll's delegate types are not in the model at
// transpile time. That is why the decision is made from the ctor signature
// (`Void .ctor(System.Object, <native int>)`) rather than a name list -- and why
// this probe exists: to prove the shape really is universal.
//
// usage: DelegateProbe <assembly.dll> <out.tsv>
//   columns: fullName, public|nonpublic, ctorShape
//   ctorShape: obj,intptr | obj,valuetype:<name> | other | none

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class P {
    static int Main(string[] a) {
        using var fs = File.OpenRead(a[0]);
        using var pe = new PEReader(fs);
        var r = pe.GetMetadataReader();
        var rows = new List<string>();
        foreach (var th in r.TypeDefinitions) {
            var td = r.GetTypeDefinition(th);
            var baseName = "";
            if (!td.BaseType.IsNil) {
                if (td.BaseType.Kind == HandleKind.TypeReference) {
                    var tr = r.GetTypeReference((TypeReferenceHandle)td.BaseType);
                    baseName = r.GetString(tr.Namespace) + "." + r.GetString(tr.Name);
                } else if (td.BaseType.Kind == HandleKind.TypeDefinition) {
                    var bt = r.GetTypeDefinition((TypeDefinitionHandle)td.BaseType);
                    baseName = r.GetString(bt.Namespace) + "." + r.GetString(bt.Name);
                }
            }
            if (baseName != "System.MulticastDelegate") continue;
            var name = r.GetString(td.Name);
            var ns = r.GetString(td.Namespace);
            var decl = td.GetDeclaringType();
            string full;
            if (!decl.IsNil) {
                var dt = r.GetTypeDefinition(decl);
                full = r.GetString(dt.Namespace) + "." + r.GetString(dt.Name) + "+" + name;
            } else {
                full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }
            var vis = (td.Attributes & TypeAttributes.VisibilityMask);
            var pub = vis == TypeAttributes.Public || vis == TypeAttributes.NestedPublic;
            rows.Add(full + "\t" + (pub ? "public" : "nonpublic") + "\t" + CtorShape(r, td));
        }
        rows.Sort(StringComparer.Ordinal);
        File.WriteAllLines(a[1], rows);
        Console.WriteLine("delegates=" + rows.Count);
        return 0;
    }

    // The shape of the delegate's .ctor, in the terms the signature test uses.
    static string CtorShape(MetadataReader r, TypeDefinition td) {
        foreach (var mh in td.GetMethods()) {
            var md = r.GetMethodDefinition(mh);
            if (r.GetString(md.Name) != ".ctor") continue;
            var sig = r.GetBlobReader(md.Signature);
            var header = sig.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method || !header.IsInstance) continue;
            var count = sig.ReadCompressedInteger();
            if (count != 2) continue;
            if (sig.ReadByte() != 0x01) continue; // ELEMENT_TYPE_VOID
            if (sig.ReadByte() != 0x1C) continue; // ELEMENT_TYPE_OBJECT
            var p1 = sig.ReadByte();
            if (p1 == 0x18) return "obj,intptr"; // ELEMENT_TYPE_I
            if (p1 == 0x11) {                    // ELEMENT_TYPE_VALUETYPE
                var h = sig.ReadTypeHandle();
                var n = h.Kind switch {
                    HandleKind.TypeReference => r.GetString(r.GetTypeReference((TypeReferenceHandle)h).Namespace)
                        + "." + r.GetString(r.GetTypeReference((TypeReferenceHandle)h).Name),
                    HandleKind.TypeDefinition => r.GetString(r.GetTypeDefinition((TypeDefinitionHandle)h).Namespace)
                        + "." + r.GetString(r.GetTypeDefinition((TypeDefinitionHandle)h).Name),
                    _ => "?",
                };
                return "obj,valuetype:" + n;
            }
            return "other";
        }
        return "none";
    }
}
