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
        string[] want = args[1].Split(',');
        foreach (var tdH in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdH);
            var owner = md.GetString(td.Namespace) + "." + md.GetString(td.Name);
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                var name = md.GetString(m.Name);
                foreach (var w in want)
                {
                    if ((owner + "." + name).Contains(w))
                    {
                        var reader = md.GetBlobReader(m.Signature);
                        var bytes = reader.ReadBytes(reader.Length);
                        Console.WriteLine($"{owner}.{name}: {BitConverter.ToString(bytes)}");
                    }
                }
            }
        }
    }
}
