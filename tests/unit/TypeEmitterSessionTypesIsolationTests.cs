using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// D1 guard #2: TypeEmitter.SessionTypes is the per-Generate type list that the
// Linq/List/Collections/HashSet key-carrier routing consults to decide whether
// a value-type key has a runtime carrier (user enum -> int32 value__) or must
// stay loud. ResolveTypeCppNameCollisions installs it once per Generate and
// emission is synchronous per body, so it is an AMBIENT, single-thread-scoped
// value -- never a process-wide one.
//
// Before the [ThreadStatic] storage fix this was a plain static, so a test that
// installs a synthetic list (MethodMapTestsGroupByKey.SessionTypesScope) raced
// any parallel test class reading it, which surfaced as a real flake:
//   NotSupportedException ... a value-type (Cases.Color) key has no runtime
//   key carrier
// in the full parallel suite while the same test passed alone.
//
// This regression test is deterministic (no retry loop): both writers install
// their own list, rendezvous on a barrier so BOTH writes are visible before ANY
// read, then read back. On process-wide storage both threads observe the same
// (last-written) list, so at least one assertion always fails. On thread-affine
// storage each thread observes exactly its own list.
public class TypeEmitterSessionTypesIsolationTests
{
    private static readonly PropertyInfo SessionTypesProperty =
        typeof(TypeEmitter).GetProperty("SessionTypes", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo SessionTypesSetter =
        SessionTypesProperty.GetSetMethod(nonPublic: true)!;

    private static IReadOnlyList<TypeDef>? ReadSessionTypes() =>
        (IReadOnlyList<TypeDef>?)SessionTypesProperty.GetValue(null);

    private static void WriteSessionTypes(IReadOnlyList<TypeDef>? value) =>
        SessionTypesSetter.Invoke(null, new object?[] { value });

    [Fact]
    public void SessionTypes_IsThreadAffine_ConcurrentWritersDoNotClobberEachOther()
    {
        var listA = new[]
        {
            new TypeDef { Namespace = "Cases", Name = "Color", BaseTypeName = "System.Enum", IsValueType = true },
        };
        var listB = new[]
        {
            new TypeDef { Namespace = "Cases", Name = "Point", BaseTypeName = "System.ValueType", IsValueType = true },
        };

        var previous = ReadSessionTypes();

        var seen = new IReadOnlyList<TypeDef>?[2];
        using var rendezvous = new Barrier(3);

        void Worker(int slot, IReadOnlyList<TypeDef> mine)
        {
            WriteSessionTypes(mine);
            // Both writes happen-before either read: no scheduler luck needed.
            rendezvous.SignalAndWait();
            seen[slot] = ReadSessionTypes();
            rendezvous.SignalAndWait();
        }

        var threadA = new Thread(() => Worker(0, listA));
        var threadB = new Thread(() => Worker(1, listB));
        try
        {
            threadA.Start();
            threadB.Start();
            rendezvous.SignalAndWait();
            rendezvous.SignalAndWait();
            Assert.True(threadA.Join(TimeSpan.FromSeconds(30)), "worker A did not finish");
            Assert.True(threadB.Join(TimeSpan.FromSeconds(30)), "worker B did not finish");

            Assert.Same(listA, seen[0]);
            Assert.Same(listB, seen[1]);
            Assert.Equal("Color", Assert.Single(seen[0]!).Name);
            Assert.Equal("Point", Assert.Single(seen[1]!).Name);
        }
        finally
        {
            WriteSessionTypes(previous);
        }
    }

    [Fact]
    public void SessionTypes_ThreadLocalValueDoesNotLeakToAFreshThread()
    {
        var mine = new[]
        {
            new TypeDef { Namespace = "Cases", Name = "Color", BaseTypeName = "System.Enum", IsValueType = true },
        };

        var previous = ReadSessionTypes();
        IReadOnlyList<TypeDef>? seenFromOtherThread = null;
        try
        {
            WriteSessionTypes(mine);
            Assert.Same(mine, ReadSessionTypes());

            var other = new Thread(() => seenFromOtherThread = ReadSessionTypes());
            other.Start();
            Assert.True(other.Join(TimeSpan.FromSeconds(30)), "reader thread did not finish");

            Assert.Null(seenFromOtherThread);
            Assert.Same(mine, ReadSessionTypes());
        }
        finally
        {
            WriteSessionTypes(previous);
        }
    }
}
