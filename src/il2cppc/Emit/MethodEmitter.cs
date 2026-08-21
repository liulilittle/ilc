namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodEmitter -- C++ function shell for one transpiled method.
//
// M1 shape:      IL2CPP_NOINLINE {ret} {name}(int32_t arg0, ...) { body }
// M2 extension:  parameter types come from the caller (ParamTypesCpp) so
//                instance methods can declare `Il2CppObject* arg0` (this)
//                and reference-typed arguments; the body is emitted verbatim
//                (it already carries its own one-level indentation and
//                right-aligned IL provenance comments -- no re-indentation).
//
// IL2CPP_NOINLINE marks the entry of every transpiled method: it keeps the
// generated function symbol addressable/visible (debuggability + the frame
// record hooks of Task 10) while leaving the COMPILER free to inline the
// calls inside the body -- AOT performance (iron rule 2e) comes from the
// C++ optimizer, not from blocking it.
//------------------------------------------------------------------------------
public static class MethodEmitter
{
    public static string EmitMethod(string cppName, string[] paramNames, string returnType, string body, string[]? paramTypes = null)
    {
        var w = new CppWriter();
        var types = paramTypes ?? Enumerable.Repeat("int32_t", paramNames.Length).ToArray();
        var paramsList = paramNames.Length == 0
            ? "void"
            : string.Join(", ", paramNames.Select((p, i) => $"{types[i]} {p}"));
        w.Line($"IL2CPP_NOINLINE {returnType} {cppName}({paramsList})");
        w.Line("{");
        foreach (var line in body.TrimEnd('\r', '\n').Split('\n'))
            w.RawLine(line.TrimEnd('\r'));
        w.Line("}");
        return w.ToString();
    }
}
