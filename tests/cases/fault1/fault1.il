.assembly extern mscorlib { .ver 4:0:0:0 }
.assembly fault1 { }
.module fault1.exe
.class public auto ansi beforefieldinit Program extends [mscorlib]System.Object {
  .method public static void Main() cil managed {
    .entrypoint
    .maxstack 8
    call void Program::NormProc()
    .try
    {
      call void Program::ThrowProc()
      ldstr "main-past"
      call void [mscorlib]System.Console::WriteLine(string)
      leave.s DONE
    }
    catch [mscorlib]System.Exception
    {
      ldstr "main-caught"
      call void [mscorlib]System.Console::WriteLine(string)
      leave.s DONE
    }
    DONE:
    ret
  }
  .method public static void NormProc() cil managed {
    .maxstack 8
    .try
    {
      ldstr "np-before"
      call void [mscorlib]System.Console::WriteLine(string)
      leave.s AFTER
    }
    fault
    {
      ldstr "np-fault"
      call void [mscorlib]System.Console::WriteLine(string)
      endfinally
    }
    AFTER:
    ldstr "np-after"
    call void [mscorlib]System.Console::WriteLine(string)
    ret
  }
  .method public static void ThrowProc() cil managed {
    .maxstack 8
    .try
    {
      ldstr "tp-before"
      call void [mscorlib]System.Console::WriteLine(string)
      ldstr "boom"
      newobj instance void [mscorlib]System.Exception::.ctor(string)
      throw
    }
    fault
    {
      ldstr "tp-fault"
      call void [mscorlib]System.Console::WriteLine(string)
      endfinally
    }
    ret
  }
}