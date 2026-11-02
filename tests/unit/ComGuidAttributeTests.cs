using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// GuidAttributeValue -- the [Guid(string)] custom attribute decodes to the 16
// IID bytes. The string form used to be mis-decoded: the SerString's first byte
// IS the start of the compressed-uint length, but the old code consumed it as a
// null marker and then read the following TEXT as the length -> the BlobReader
// threw "Read out of bounds" the first time a real [Guid(string)] type reached
// it (the byval COM P/Invoke parameter guard). tests/fixtures/comguid carries
// the string form.
//
// The test ALSO proves the fixture really selects the SerString branch by
// decoding the attribute constructor signature and asserting paramCount == 1.
// (.NET Framework 4.8's GuidAttribute has ONLY the (string) ctor -- CS1729
// rejects an 11-argument [Guid(...)] -- so the GuidAttributeValue paramCount==11
// branch is unreachable from C# and cannot have a C#-emitted fixture.)
public class ComGuidAttributeTests
{
    private const string IidText = "8F2A1B3C-4D5E-6F70-8192-A3B4C5D6E7F8";

    [Fact]
    public void GuidAttributeValue_StringSerString_DecodesSixteenBytes()
    {
        var path = Path.Combine(RepoRoot(), "tests", "fixtures", "out", "comguid.dll");
        Assert.True(File.Exists(path),
            $"fixture not found: {path} -- run scripts/build-fixtures.ps1 first");

        using var pe = new PEReader(File.OpenRead(path));
        var md = pe.GetMetadataReader();

        var expected = Guid.Parse(IidText).ToByteArray();

        TypeDefinitionHandle found = default;
        foreach (var th in md.TypeDefinitions)
        {
            if (md.GetFullName(th) == "IStrGuid")
            {
                found = th;
                break;
            }
        }
        Assert.False(found.IsNil, "IStrGuid TypeDef not found in the comguid fixture assembly");

        var td = md.GetTypeDefinition(found);

        // PRECONDITION: the [Guid] ctor carries exactly ONE parameter, i.e. the
        // string SerString form -- this is the branch under test.
        Assert.Equal(1, GuidAttributeCtorParamCount(md, td));

        var bytes = MetadataLoader.GuidAttributeValue(md, td.GetCustomAttributes());

        Assert.NotNull(bytes);
        Assert.Equal(16, bytes!.Length);
        Assert.Equal(expected, bytes);
    }

    // Decode the GuidAttribute custom-attribute constructor signature's fixed
    // argument count. -1 when no GuidAttribute is present.
    private static int GuidAttributeCtorParamCount(MetadataReader md, TypeDefinition td)
    {
        foreach (var cah in td.GetCustomAttributes())
        {
            var ca = md.GetCustomAttribute(cah);
            if (ca.Constructor.Kind is not (HandleKind.MemberReference or HandleKind.MethodDefinition))
                continue;

            EntityHandle attrType;
            BlobHandle sigHandle;
            if (ca.Constructor.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                attrType = mr.Parent;
                sigHandle = mr.Signature;
            }
            else
            {
                var mdef = md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                attrType = mdef.GetDeclaringType();
                sigHandle = mdef.Signature;
            }

            var attrName = attrType.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)attrType),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)attrType),
                _ => "",
            };
            if (attrName != "System.Runtime.InteropServices.GuidAttribute") continue;

            var sig = md.GetBlobReader(sigHandle);
            sig.ReadByte(); // calling convention
            return sig.ReadCompressedInteger();
        }
        return -1;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "il2cppc", "il2cppc.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("cannot locate the repo root from " + AppContext.BaseDirectory);
    }
}