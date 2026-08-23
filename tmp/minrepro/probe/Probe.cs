using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class Probe
{
    static void Main(string[] args)
    {
        using var fs = new System.IO.FileStream(args[0], System.IO.FileMode.Open, System.IO.FileAccess.Read);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        foreach (var mh in md.MethodDefinitions)
        {
            var mdDef = md.GetMethodDefinition(mh);
            var body = md.GetMethodBody(mdDef.RelativeVirtualAddress);
            foreach (var ih in body.Instructions)
            {
                if (ih.OpCode.Code == System.Reflection.Emit.OpCodes.Newobj.Value)
                {
                    var token = MetadataTokens.GetToken((EntityHandle)ih.Operand);
                    var handle = (EntityHandle)ih.Operand;
                    string parentDesc = "?";
                    if (handle.Kind == HandleKind.MemberReference)
                    {
                        var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                        parentDesc = mr.Parent.Kind.ToString();
                        if (mr.Parent.Kind == HandleKind.TypeSpecification)
                        {
                            var ts = md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
                            var blob = md.GetBlobReader(ts.Signature);
                            var b0 = blob.ReadByte();
                            parentDesc += $" byte=0x{b0:X2}";
                            if (b0 == 0x14)
                            {
                                Console.WriteLine($"FNPTR newobj in method '{md.GetString(mdDef.Name)}' of type '{md.GetString(md.GetMethodDefinition(mh).GetDeclaringType() is { } d ? d : default)}' at IL_{ih.Offset:X4}");
                            }
                        }
                    }
                }
            }
        }
        Console.WriteLine("probe done");
    }
}
