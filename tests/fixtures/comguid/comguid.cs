// comguid.cs -- unit-test fixture for the [Guid] custom-attribute decoder.
//
// GuidAttributeValue decodes the [Guid(string)] SerString form. That form used
// to be mis-decoded (the SerString's first length byte was consumed as a null
// marker, then the TEXT was read as the length -> BlobReader "Read out of
// bounds"). This fixture pins the string form against a real csc build.
//
// NOTE: .NET Framework 4.8's GuidAttribute has ONLY the (string) constructor
// (CS1729 proves an 11-argument [Guid(...)] does not compile), so the
// GuidAttributeValue paramCount==11 branch is unreachable from C#; it remains
// defensive for assemblies emitted by other toolchains.
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("8F2A1B3C-4D5E-6F70-8192-A3B4C5D6E7F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStrGuid
{
    int GetValue();
}

public static class ComGuidFixture
{
    public static void Main()
    {
        Console.WriteLine("comguid");
    }
}