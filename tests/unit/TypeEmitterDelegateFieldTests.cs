using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterDelegateFieldTests -- a field of a BCL delegate type is a plain GC
// reference slot.
//
// The runtime stamps EVERY delegate with its single System.Delegate class
// (delegate.cpp g_delegateClass), so no per-delegate C++ struct exists. A field
// declared as one must therefore be Il2CppObject*, not {Sanitize(name)}* --
// the latter names an undefined type and fails the C++ build.
//
// The BCL delegate families are not in the model (the transpiler loads only the
// user assembly), so ReferenceCppType cannot ask "does this derive from
// MulticastDelegate?". It asks MethodRefResolver.IsBclDelegateType, the one
// gated list (tools/audit/bcl_delegate_gate.py re-derives it from mscorlib +
// System.dll metadata in CI).
//
// This used to be a fifth hardcoded copy naming only System.EventHandler and
// System.EventHandler`1, so every other family fell through to the generic
// "not in the types list" fallback. That fallback happens to answer the same
// thing, which is why the defect was invisible: the arm was redundant, not
// wrong. These tests pin the ANSWER for each family so the arm cannot be
// removed or narrowed without a red test.
//------------------------------------------------------------------------------
public class TypeEmitterDelegateFieldTests
{
    // A user type list that does NOT contain the BCL delegate -- the shape the
    // transpiler actually has (only the user assembly is loaded).
    private static IReadOnlyList<TypeDef> UserTypes()
    {
        var t = new TypeDef { Name = "Holder", Namespace = "App" };
        return new[] { t };
    }

    // The PREDICATE itself. This is the assertion that can go red:
    // ReferenceCppType has a generic "not in the types list" fallback that
    // also answers Il2CppObject*, so a field-type test alone cannot tell the
    // delegate arm from the fallback (verified: forcing IsBclDelegateType to
    // false left the field-type theory green).
    [Theory]
    // mscorlib families
    [InlineData("System.Action")]
    [InlineData("System.Action`1")]
    [InlineData("System.Func`2")]
    [InlineData("System.Predicate`1")]
    [InlineData("System.Converter`2")]
    [InlineData("System.Comparison`1")]
    [InlineData("System.EventHandler")]
    [InlineData("System.EventHandler`1")]
    [InlineData("System.AsyncCallback")]
    [InlineData("System.Threading.ThreadStart")]
    [InlineData("System.Threading.WaitCallback")]
    [InlineData("System.Threading.ParameterizedThreadStart")]
    [InlineData("System.ResolveEventHandler")]
    [InlineData("System.UnhandledExceptionEventHandler")]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpers+TryCode")]
    // System.dll families -- the ones the old two-name list missed
    [InlineData("System.ComponentModel.PropertyChangedEventHandler")]
    [InlineData("System.Reflection.MemberFilter")]
    [InlineData("System.Reflection.TypeFilter")]
    [InlineData("System.Reflection.ModuleResolveEventHandler")]
    [InlineData("System.Timers.ElapsedEventHandler")]
    [InlineData("System.Threading.ThreadExceptionEventHandler")]
    public void BclDelegateField_IsGcReferenceSlot(string delegateType)
    {
        var cpp = TypeEmitter.ReferenceCppType(delegateType, UserTypes());
        Assert.Equal("Il2CppObject*", cpp);
    }

    // The same families, asserted on the PREDICATE. This is the red-able
    // half: it fails the moment the gated list loses a family.
    [Theory]
    [InlineData("System.Action")]
    [InlineData("System.Action`1")]
    [InlineData("System.Func`2")]
    [InlineData("System.Predicate`1")]
    [InlineData("System.Converter`2")]
    [InlineData("System.Comparison`1")]
    [InlineData("System.EventHandler")]
    [InlineData("System.EventHandler`1")]
    [InlineData("System.AsyncCallback")]
    [InlineData("System.Threading.ThreadStart")]
    [InlineData("System.Threading.WaitCallback")]
    [InlineData("System.Threading.ParameterizedThreadStart")]
    [InlineData("System.ResolveEventHandler")]
    [InlineData("System.UnhandledExceptionEventHandler")]
    [InlineData("System.Runtime.CompilerServices.RuntimeHelpers+TryCode")]
    [InlineData("System.ComponentModel.PropertyChangedEventHandler")]
    [InlineData("System.Reflection.MemberFilter")]
    [InlineData("System.Reflection.TypeFilter")]
    [InlineData("System.Reflection.ModuleResolveEventHandler")]
    [InlineData("System.Timers.ElapsedEventHandler")]
    [InlineData("System.Threading.ThreadExceptionEventHandler")]
    public void IsBclDelegateType_RecognisesFamily(string delegateType)
    {
        Assert.True(MethodRefResolver.IsBclDelegateType(delegateType));
    }

    // The guard against over-broadening: a NON-delegate BCL type that is also
    // absent from the model must not be claimed by the delegate arm. It still
    // answers Il2CppObject* (the generic fallback), so this asserts the
    // predicate directly rather than the emitted type.
    [Theory]
    [InlineData("System.String")]
    [InlineData("System.Object")]
    [InlineData("System.ComponentModel.PropertyChangedEventArgs")]
    [InlineData("System.Reflection.MethodInfo")]
    [InlineData("System.Runtime.InteropServices.HandleRef")]
    public void NonDelegateBclType_IsNotBclDelegate(string typeName)
    {
        Assert.False(MethodRefResolver.IsBclDelegateType(typeName));
    }

    // A user-declared delegate is answered by the TypeDef base chain, not the
    // BCL list -- and its field is also a GC reference slot.
    [Fact]
    public void UserDelegateField_IsGcReferenceSlot()
    {
        var del = new TypeDef { Name = "ChainFn", Namespace = "App", BaseTypeName = "System.MulticastDelegate" };
        var holder = new TypeDef { Name = "Holder", Namespace = "App" };
        var types = new[] { del, holder };

        Assert.Equal("Il2CppObject*", TypeEmitter.ReferenceCppType("App.ChainFn", types));
        Assert.False(MethodRefResolver.IsBclDelegateType("App.ChainFn"));
    }
}
