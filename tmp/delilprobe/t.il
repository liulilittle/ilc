
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0




// Metadata version: v4.0.30319
.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 4:0:0:0
}
.assembly t
{
  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilationRelaxationsAttribute::.ctor(int32) = ( 01 00 08 00 00 00 00 00 ) 
  .custom instance void [mscorlib]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::.ctor() = ( 01 00 01 00 54 02 16 57 72 61 70 4E 6F 6E 45 78   // ....T..WrapNonEx
                                                                                                             63 65 70 74 69 6F 6E 54 68 72 6F 77 73 01 )       // ceptionThrows.
  .hash algorithm 0x00008004
  .ver 0:0:0:0
}
.module t.exe
// MVID: {62D2C250-7EC9-4A26-97EA-DD91E1CBCAFF}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x07010000


// =============== CLASS MEMBERS DECLARATION ===================

.class public auto ansi beforefieldinit T
       extends [mscorlib]System.Object
{
  .method public hidebysig static int32  A(int32 x) cil managed
  {
    // 代码大小       9 (0x9)
    .maxstack  2
    .locals init (int32 V_0)
    IL_0000:  nop
    IL_0001:  ldarg.0
    IL_0002:  ldc.i4.1
    IL_0003:  add
    IL_0004:  stloc.0
    IL_0005:  br.s       IL_0007

    IL_0007:  ldloc.0
    IL_0008:  ret
  } // end of method T::A

  .method public hidebysig static void  Main() cil managed
  {
    .entrypoint
    // 代码大小       29 (0x1d)
    .maxstack  2
    .locals init (class [mscorlib]System.Func`2<int32,int32> V_0,
             int32 V_1)
    IL_0000:  nop
    IL_0001:  ldnull
    IL_0002:  ldftn      int32 T::A(int32)
    IL_0008:  newobj     instance void class [mscorlib]System.Func`2<int32,int32>::.ctor(object,
                                                                                         native int)
    IL_000d:  stloc.0
    IL_000e:  ldloc.0
    IL_000f:  callvirt   instance int32 [mscorlib]System.Object::GetHashCode()
    IL_0014:  stloc.1
    IL_0015:  ldloc.1
    IL_0016:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_001b:  nop
    IL_001c:  ret
  } // end of method T::Main

  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // 代码大小       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void [mscorlib]System.Object::.ctor()
    IL_0006:  ret
  } // end of method T::.ctor

} // end of class T


// =============================================================

// *********** 反汇编完成 ***********************
// 警告: 创建了 Win32 资源文件 E:\il2cpp\tmp\delilprobe\t.res
