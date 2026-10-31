using Il2Cpp.Transpiler.Emit;
using Xunit;

// D1 guard: the generic-clone "current body" hint is AMBIENT context. It is
// written/read around a single synchronous body pass, so it must be private to
// the thread running that pass. xUnit runs test classes in parallel
// collections; before the [ThreadStatic] storage fix these were process-wide
// statics, so two tests (or two parallel emit passes) clobbered each other's
// save/set/restore and the loser silently translated typeof(T) / clone field
// types with a foreign instantiation suffix.
//
// This test is deterministic (no retry loops): both writers set their hint,
// rendezvous on a barrier so BOTH writes are visible before ANY read, then
// read back. On process-wide storage both threads must observe the same
// (last-written) value, so at least one assertion always fails. On
// thread-affine storage each thread observes exactly its own value.
public class MethodRefResolverHintIsolationTests
{
    [Fact]
    public void InstantiationSuffixHint_IsThreadAffine_ConcurrentWritersDoNotClobberEachOther()
    {
        const string hintA = "clone_a`2_string_decimal";
        const string hintB = "clone_b`1_int";
        string[] partsA = { "string", "decimal" };
        string[] partsB = { "int" };

        var prevHint = MethodRefResolver.InstantiationSuffixHint;
        var prevParts = MethodRefResolver.InstantiationSuffixHintParts;

        var seenHints = new string?[2];
        var seenParts = new string[]?[2];
        using var rendezvous = new Barrier(3);

        void Worker(int slot, string hint, string[] parts)
        {
            MethodRefResolver.InstantiationSuffixHint = hint;
            MethodRefResolver.InstantiationSuffixHintParts = parts;
            // Both writes happen-before either read: no scheduler luck needed.
            rendezvous.SignalAndWait();
            seenHints[slot] = MethodRefResolver.InstantiationSuffixHint;
            seenParts[slot] = MethodRefResolver.InstantiationSuffixHintParts;
            rendezvous.SignalAndWait();
        }

        var threadA = new Thread(() => Worker(0, hintA, partsA));
        var threadB = new Thread(() => Worker(1, hintB, partsB));
        try
        {
            threadA.Start();
            threadB.Start();
            rendezvous.SignalAndWait();
            rendezvous.SignalAndWait();
            Assert.True(threadA.Join(TimeSpan.FromSeconds(30)), "worker A did not finish");
            Assert.True(threadB.Join(TimeSpan.FromSeconds(30)), "worker B did not finish");

            Assert.Equal(hintA, seenHints[0]);
            Assert.Equal(partsA, seenParts[0]);
            Assert.Equal(hintB, seenHints[1]);
            Assert.Equal(partsB, seenParts[1]);
        }
        finally
        {
            MethodRefResolver.InstantiationSuffixHint = prevHint;
            MethodRefResolver.InstantiationSuffixHintParts = prevParts;
        }
    }
}
