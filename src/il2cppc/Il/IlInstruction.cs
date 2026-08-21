using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Il;

public enum IlOperandKind
{
    None,
    BranchTarget,
    FieldToken,
    MethodToken,
    StringToken,
    TypeToken,
    Int8,
    Int16,
    Int32,
    Int64,
    Float,
    Double,
    Switch
}

public readonly struct IlInstruction
{
    public ILOpCode OpCode { get; init; }
    public int Offset { get; init; }
    public IlOperandKind OperandKind { get; init; }
    public long Operand { get; init; }      // constant value/token/branch target; Switch = table size N
    public string? StringOperand { get; init; } // string constant (M1 does not parse it; Task 5/6 fills it via MetadataReader)
    // switch(0x45): N absolute jump targets (table order; possibly out-of-order/unevenly
    // spaced -- the compiler emits the table in case order). The default arm = the next
    // instruction after switch (out-of-range/negative values land there). The conversion
    // of table entries relative to the "instruction end" is done at decode time;
    // consumers only read absolute offsets.
    public int[]? SwitchTargets { get; init; }
}
