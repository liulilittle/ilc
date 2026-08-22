using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
class Probe3 {
    static void Main(string[] args) {
        using var pe = new PEReader(System.IO.File.OpenRead(args[0]));
        var md = pe.GetMetadataReader();
        foreach (var mrh in md.MemberReferences) {
            var mr = md.GetMemberReference(mrh);
            var nm = md.GetString(mr.Name);
            if (nm == "GetResult" || nm == "SetResult" || nm == "get_Result") {
                Console.WriteLine($"{nm}: parent={mr.Parent.Kind}");
            }
        }
    }
}
