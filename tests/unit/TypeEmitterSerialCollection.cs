using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterSerial -- serializes the tests that drive TypeEmitter's static
// session state (_cppNameOverrides + the H3 nested-type index) directly.
//
// TypeEmitter.ResolveTypeCppNameCollisions writes process-wide statics and is
// normally called by CodeGenerator.Generate, which holds CodeGenerator's
// private _poolLock. Tests that call it themselves bypass that lock, and xUnit
// runs test CLASSES in parallel by default -- so such a test could swap the
// override map (or clear the TypeCppName memo) while a Generate-based test in
// another class is mid-emission, yielding a silently stale bare C++ name.
//
// DisableParallelization=true makes every class in this collection run
// sequentially with respect to the rest of the suite, which closes that hole
// without touching production code. The individual tests also restore the
// static state in a finally block (see ResetSessionState), so a failure cannot
// leak a collision map into the next test.
//------------------------------------------------------------------------------
[CollectionDefinition("TypeEmitterSerial", DisableParallelization = true)]
public class TypeEmitterSerialCollection
{
}
