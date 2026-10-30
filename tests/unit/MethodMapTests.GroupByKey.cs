using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// impl-groupbykey: the GroupBy KEY-FAMILY matrix (b5-gaps 17.2 (3)).
//
// Baseline LOUDs this batch closes:
//   * GroupBy<int32,TimeSpan>       -> "a value-type (System.TimeSpan) key has
//                                      no runtime key carrier";
//   * GroupBy<int64,DateTime>       -> "the stage-1 runtime LINQ surface is
//                                      int32 (0x08) element-family";
//   * GroupBy<int32,Cases.Color>    -> "a value-type (Cases.Color) key ...".
//
// The mapping contract asserted here (MUST stay in lockstep with the runtime
// switch in src/runtime/src/linq_map.cpp):
//   keyKind 12 System.TimeSpan       -- the ticks carrier;
//   keyKind 13 System.DateTimeOffset -- the UTC-instant carrier (+ offset);
//   keyKind 14 a USER ENUM           -- the int32 value__ carrier;
//   srcKind  5 int64 / 6 double / 7 narrow (1/2/4-byte) sources ride the same
//            key-family dispatch (narrow adds the trailing source CELL code).
//
// The kernel SEMANTICS (decimal compares by VALUE, DateTime/DateTimeOffset by
// the INSTANT, TimeSpan/enum by their payload) live in the C++ runtime and are
// locked by the e2e probe `tests/cases/probe-groupbykey` (oracle-vs-runtime
// sha equality), not by this C# mapping suite.
public class MethodMapTestsGroupByKey
{
    // ---------------------------------------------------------------- keys --

    // A 0x11 key whose TypeRef names a wireable BCL value type resolves to the
    // matching keyKind instead of the "value-type key" LOUD.
    [Theory]
    [InlineData("System", "TimeSpan", "12")]
    [InlineData("System", "DateTimeOffset", "13")]
    [InlineData("System", "DateTime", "9")]   // regression: unchanged
    [InlineData("System", "Guid", "10")]      // regression: unchanged
    [InlineData("System", "Decimal", "8")]    // regression: unchanged
    public void GroupBy_NamedValueTypeKey_MapsKeyKind(string ns, string typeName, string keyKind)
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x08, keyTypeName: (ns, typeName), keyByte: 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "0", "0", keyKind, "0" }, resolved.AppendLiterals);
    }

    // A USER ENUM key: TypeEmitter's session type list says the TypeDef's base
    // is System.Enum, so the key rides the int32 value__ carrier (keyKind 14).
    [Fact]
    public void GroupBy_UserEnumKey_MapsKeyKind14()
    {
        using var session = new SessionTypesScope(new[]
        {
            new TypeDef { Namespace = "Cases", Name = "Color", BaseTypeName = "System.Enum",
                IsValueType = true },
        });

        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x08, keyTypeName: ("Cases", "Color"), keyByte: 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(new[] { "0", "0", "14", "0" }, resolved.AppendLiterals);
    }

    // A USER STRUCT key (same 0x11 byte, but NOT an enum) has no by-value key
    // carrier ABI, so it stays LOUD -- the enum widening must never swallow it.
    [Fact]
    public void GroupBy_UserStructKey_StaysLoud()
    {
        using var session = new SessionTypesScope(new[]
        {
            new TypeDef { Namespace = "Cases", Name = "Point", BaseTypeName = "System.ValueType",
                IsValueType = true },
        });

        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x08, keyTypeName: ("Cases", "Point"), keyByte: 0x11)));

        Assert.Contains("has no runtime key carrier", ex.Message);
    }

    // ------------------------------------------------------- wide sources --

    // GroupBy over a long[] source: the shared kernel reads 8-byte cells
    // (srcKind 5) and keeps the boxed-int64 bucket for the key-only overload.
    [Fact]
    public void GroupBy_Int64Source_MapsSharedKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x0A, keyTypeName: null, keyByte: 0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(new[] { "2", "0", "0", "5" }, resolved.AppendLiterals);
    }

    [Fact]
    public void GroupBy_DoubleSource_MapsSharedKernel()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x0D, keyTypeName: null, keyByte: 0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(new[] { "3", "0", "0", "6" }, resolved.AppendLiterals);
    }

    // A NARROW (1/2/4-byte) source needs the SOURCE CELL code, so it rides the
    // _cell entry: byte[] -> cell 1.
    [Fact]
    public void GroupBy_ByteSource_UsesCellEntry()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x05, keyTypeName: null, keyByte: 0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key_cell", resolved.CppName);
        Assert.Equal(new[] { "0", "0", "0", "7", "1" }, resolved.AppendLiterals);
    }

    // GroupBy<int64,DateTime> -- the exact shape b5-gaps 17.2 (3) registered.
    [Fact]
    public void GroupBy_Int64Source_DateTimeKey_MapsKeyKind9()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x0A,
            keyTypeName: ("System", "DateTime"), keyByte: 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(new[] { "2", "0", "9", "5" }, resolved.AppendLiterals);
    }

    // GroupBy<int32,TimeSpan> -- the other registered shape, over the int32
    // source family, stays on the plain entry (srcKind 0).
    [Fact]
    public void GroupBy_Int32Source_TimeSpanKey_MapsKeyKind12()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x08,
            keyTypeName: ("System", "TimeSpan"), keyByte: 0x11));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(new[] { "0", "0", "12", "0" }, resolved.AppendLiterals);
    }

    // A float[] source has no bucket cell ABI (the Single element family has no
    // boxed bucket carrier), so it keeps the LOUD instead of a wrong bucket.
    [Fact]
    public void GroupBy_FloatSource_StaysLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeGroupBy(
            srcTypeName: null, srcByte: 0x0C, keyTypeName: null, keyByte: 0x08)));

        Assert.Contains("int32 (0x08) element-family", ex.Message);
    }

    // ------------------------------------------------- marker / get_Key ------

    [Theory]
    [InlineData("System.TimeSpan", "_key_ts")]
    [InlineData("System.DateTimeOffset", "_key_dto")]
    [InlineData("System.DateTime", "_key_dt")]   // regression
    [InlineData("System.Guid", "_key_guid")]     // regression
    public void LinqGroupingKeyMarker_NewFamilies(string keyTypeName, string expected)
    {
        var mi = typeof(MethodRefResolver).GetMethod("LinqGroupingKeyMarker",
            BindingFlags.NonPublic | BindingFlags.Static);
        var actual = (string)mi!.Invoke(null, new object?[] { keyTypeName })!;

        Assert.Equal(expected, actual);
    }

    // get_Key dispatches on the tracked grouping type's key-family marker, so a
    // TimeSpan / DateTimeOffset bucket reads its REAL carrier instead of the
    // int32 accessor (which would hand back the low dword of the ticks).
    [Theory]
    [InlineData("System.Linq.IGrouping_key_ts", "il2cpp::il2cpp_linq_grouping_get_key_ts", "ts")]
    [InlineData("System.Linq.IGrouping_key_dto", "il2cpp::il2cpp_linq_grouping_get_key_dto", "dto")]
    public void GetKey_NewKeyMarkers_MapToValueAccessors(
        string receiverType, string cpp, string resultKind)
    {
        var resolved = ResolveMemberRefWithReceiver(
            SynthesizeMemberRef("System.Linq", "IGrouping`2", "get_Key",
                new byte[] { 0x00, 0x00, 0x08 }),
            "get_Key", receiverType);

        Assert.Equal(cpp, resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(resultKind, resolved.ResultKind);
    }

    // ------------------------------------------------------------ helpers ---

    // GroupBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>) with a
    // MethodSpec whose two generic arguments are either a primitive BYTE or a
    // 0x11 VALUETYPE TypeRef (so the name table can resolve it).
    private static MemoryStream SynthesizeGroupBy(
        string? srcTypeName, byte srcByte, (string Ns, string Name)? keyTypeName, byte keyByte)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var iEnum = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var func2 = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Func`2"));
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("GroupBy"),
            mdb.GetOrAddBlob(GroupByKeyOnlySignature(iEnum, func2)));

        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        WriteTypeArg(spec, mdb, asmRef, srcTypeName, srcByte);
        WriteTypeArg(spec, mdb, asmRef, keyTypeName, keyByte);
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec));
        return Serialize(mdb);
    }

    private static void WriteTypeArg(BlobBuilder spec, MetadataBuilder mdb,
        AssemblyReferenceHandle asmRef, string? typeName, byte typeByte)
    {
        if (typeName is { } dotted)
        {
            int dot = dotted.LastIndexOf('.');
            var ns = dotted[..dot];
            var name = dotted[(dot + 1)..];
            var tr = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(name));
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(tr) << 2) | 1);
            return;
        }
        spec.WriteByte(typeByte);
    }

    private static void WriteTypeArg(BlobBuilder spec, MetadataBuilder mdb,
        AssemblyReferenceHandle asmRef, (string Ns, string Name)? named, byte typeByte)
    {
        if (named is { } n)
        {
            WriteTypeArg(spec, mdb, asmRef, n.Ns + "." + n.Name, typeByte);
            return;
        }
        spec.WriteByte(typeByte);
    }

    // GroupBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>): the
    // signature MethodMap.Linq DECODES via LinqEncodedParamTypes, so both the
    // IEnumerable`1 and Func`2 TypeRefs must be real rows.
    private static byte[] GroupByKeyOnlySignature(TypeReferenceHandle iEnum, TypeReferenceHandle func2)
    {
        var bb = new BlobBuilder();
        bb.WriteByte(0x10);
        bb.WriteCompressedInteger(2); // GenericParameterCount
        bb.WriteCompressedInteger(2); // param count
        bb.WriteByte(0x08);           // (simplified) return int32
        WriteGenericInst(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0); // VAR 0 (TSource)
        WriteGenericInst(bb, func2, 2);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1); // VAR 1 (TKey)
        return bb.ToArray();
    }

    private static void WriteGenericInst(BlobBuilder bb, TypeReferenceHandle def, int argc)
    {
        bb.WriteByte(0x15);
        bb.WriteByte(0x12); // CLASS marker
        bb.WriteCompressedInteger((MetadataTokens.GetRowNumber(def) << 2) | 1);
        bb.WriteCompressedInteger(argc);
    }

    private static MemoryStream SynthesizeMemberRef(string ns, string typeName, string memberName,
        byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var owner = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(owner, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
    }

    private static ResolvedMethod ResolveMemberRefWithReceiver(MemoryStream pe, string memberName,
        string? receiverType)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MemberReferenceHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(),
            receiverType, false, receiverType, null);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    // TypeEmitter.SessionTypes is a session-scoped static the transpiler sets
    // once per Generate; the enum-key test installs a synthetic type list and
    // restores the previous value so no other test observes it.
    private sealed class SessionTypesScope : IDisposable
    {
        private readonly IReadOnlyList<TypeDef>? _previous;

        public SessionTypesScope(IReadOnlyList<TypeDef> types)
        {
            var prop = typeof(TypeEmitter).GetProperty("SessionTypes",
                BindingFlags.Public | BindingFlags.Static)!;
            _previous = (IReadOnlyList<TypeDef>?)prop.GetValue(null);
            prop.GetSetMethod(true)!.Invoke(null, new object?[] { types });
        }

        public void Dispose()
        {
            var prop = typeof(TypeEmitter).GetProperty("SessionTypes",
                BindingFlags.Public | BindingFlags.Static)!;
            prop.GetSetMethod(true)!.Invoke(null, new object?[] { _previous });
        }
    }
}
