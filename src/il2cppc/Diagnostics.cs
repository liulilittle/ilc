namespace Il2Cpp.Transpiler;

// G2 (friendly-compiler S4): a NotSupportedException raised during a specific
// method's emission is re-wrapped into a MethodContextException that carries
// the owning type::method identity, so a top-level transpile failure names the
// exact method that rejected loudly instead of a bare exception message. The
// message is built by the generator's catch sites as
// "{TypeName}::{MethodName}: {inner.Message}" -- the inner message stays
// verbatim (existing Contains assertions on the inner text keep passing).
public sealed class MethodContextException : NotSupportedException
{
    public string? TypeName { get; }
    public string? MethodName { get; }

    public MethodContextException(string? typeName, string? methodName, string message, Exception? inner = null)
        : base(message, inner)
    {
        TypeName = typeName;
        MethodName = methodName;
    }
}
