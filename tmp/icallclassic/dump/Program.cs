using System;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class Dump
{
    static void Main(string[] args)
    {
        using var fs = new System.IO.FileStream(args[0], System.IO.FileMode.Open, System.IO.FileAccess.Read);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        int target = Convert.ToInt32(args[1].Replace("0x", ""), 16) & 0xFFFFFF;
        int idx = 0;
        foreach (var h in md.MemberReferences)
        {
            idx++;
            if (idx == target)
            {
                var mr = md.GetMemberReference(h);
                string pn = "?";
                if (mr.Parent.Kind == HandleKind.TypeReference)
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                    pn = md.GetString(tr.Namespace) + "." + md.GetString(tr.Name);
                }
                else if (mr.Parent.Kind == HandleKind.TypeDefinition)
                {
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent);
                    pn = md.GetString(td.Namespace) + "." + md.GetString(td.Name);
                }
                Console.WriteLine($"TARGET #{idx}: {pn}.{md.GetString(mr.Name)}");
            }
        }
        Console.WriteLine($"total memberrefs={idx}");
    }
}
