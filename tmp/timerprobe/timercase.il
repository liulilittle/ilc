
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0




// Metadata version: v4.0.30319
.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 4:0:0:0
}
.assembly timercase
{
  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilationRelaxationsAttribute::.ctor(int32) = ( 01 00 08 00 00 00 00 00 ) 
  .custom instance void [mscorlib]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::.ctor() = ( 01 00 01 00 54 02 16 57 72 61 70 4E 6F 6E 45 78   // ....T..WrapNonEx
                                                                                                             63 65 70 74 69 6F 6E 54 68 72 6F 77 73 01 )       // ceptionThrows.

  // --- 下列自定义特性会自动添加，不要取消注释 -------
  //  .custom instance void [mscorlib]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggableAttribute/DebuggingModes) = ( 01 00 02 00 00 00 00 00 ) 

  .custom instance void [mscorlib]System.Runtime.Versioning.TargetFrameworkAttribute::.ctor(string) = ( 01 00 1A 2E 4E 45 54 46 72 61 6D 65 77 6F 72 6B   // ....NETFramework
                                                                                                        2C 56 65 72 73 69 6F 6E 3D 76 34 2E 38 01 00 54   // ,Version=v4.8..T
                                                                                                        0E 14 46 72 61 6D 65 77 6F 72 6B 44 69 73 70 6C   // ..FrameworkDispl
                                                                                                        61 79 4E 61 6D 65 12 2E 4E 45 54 20 46 72 61 6D   // ayName..NET Fram
                                                                                                        65 77 6F 72 6B 20 34 2E 38 )                      // ework 4.8
  .custom instance void [mscorlib]System.Reflection.AssemblyCompanyAttribute::.ctor(string) = ( 01 00 09 74 69 6D 65 72 63 61 73 65 00 00 )       // ...timercase..
  .custom instance void [mscorlib]System.Reflection.AssemblyConfigurationAttribute::.ctor(string) = ( 01 00 07 52 65 6C 65 61 73 65 00 00 )             // ...Release..
  .custom instance void [mscorlib]System.Reflection.AssemblyFileVersionAttribute::.ctor(string) = ( 01 00 07 31 2E 30 2E 30 2E 30 00 00 )             // ...1.0.0.0..
  .custom instance void [mscorlib]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = ( 01 00 2E 31 2E 30 2E 30 2B 37 31 64 38 62 31 64   // ...1.0.0+71d8b1d
                                                                                                             61 32 33 30 39 38 34 63 66 30 61 39 30 62 62 38   // a230984cf0a90bb8
                                                                                                             36 35 38 64 65 34 35 64 61 61 33 31 62 38 31 61   // 658de45daa31b81a
                                                                                                             63 00 00 )                                        // c..
  .custom instance void [mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string) = ( 01 00 09 74 69 6D 65 72 63 61 73 65 00 00 )       // ...timercase..
  .custom instance void [mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string) = ( 01 00 09 74 69 6D 65 72 63 61 73 65 00 00 )       // ...timercase..
  .hash algorithm 0x00008004
  .ver 1:0:0:0
}
.module timercase.exe
// MVID: {42CDE919-F871-47AD-98F0-14940DF0B9F6}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x055E0000


// =============== CLASS MEMBERS DECLARATION ===================

.class public abstract auto ansi sealed beforefieldinit Cases.TimerCase
       extends [mscorlib]System.Object
{
  .field private static int32 g_fires
  .method private hidebysig static void  Cb(object state) cil managed
  {
    // 代码大小       13 (0xd)
    .maxstack  8
    IL_0000:  ldsfld     int32 Cases.TimerCase::g_fires
    IL_0005:  ldc.i4.1
    IL_0006:  add
    IL_0007:  stsfld     int32 Cases.TimerCase::g_fires
    IL_000c:  ret
  } // end of method TimerCase::Cb

  .method public hidebysig static void  Main() cil managed
  {
    .entrypoint
    // 代码大小       234 (0xea)
    .maxstack  4
    .locals init (class [mscorlib]System.Threading.Timer V_0)
    IL_0000:  ldnull
    IL_0001:  ldftn      void Cases.TimerCase::Cb(object)
    IL_0007:  newobj     instance void [mscorlib]System.Threading.TimerCallback::.ctor(object,
                                                                                       native int)
    IL_000c:  ldnull
    IL_000d:  ldc.i4.s   50
    IL_000f:  ldc.i4.s   100
    IL_0011:  newobj     instance void [mscorlib]System.Threading.Timer::.ctor(class [mscorlib]System.Threading.TimerCallback,
                                                                               object,
                                                                               int32,
                                                                               int32)
    IL_0016:  stloc.0
    .try
    {
      IL_0017:  ldc.i4     0x104
      IL_001c:  call       void [mscorlib]System.Threading.Thread::Sleep(int32)
      IL_0021:  leave.s    IL_002d

    }  // end .try
    finally
    {
      IL_0023:  ldloc.0
      IL_0024:  brfalse.s  IL_002c

      IL_0026:  ldloc.0
      IL_0027:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_002c:  endfinally
    }  // end handler
    IL_002d:  ldstr      "after-periodic: "
    IL_0032:  ldsflda    int32 Cases.TimerCase::g_fires
    IL_0037:  call       instance string [mscorlib]System.Int32::ToString()
    IL_003c:  call       string [mscorlib]System.String::Concat(string,
                                                                string)
    IL_0041:  call       void [mscorlib]System.Console::WriteLine(string)
    IL_0046:  ldc.i4.0
    IL_0047:  stsfld     int32 Cases.TimerCase::g_fires
    IL_004c:  ldnull
    IL_004d:  ldftn      void Cases.TimerCase::Cb(object)
    IL_0053:  newobj     instance void [mscorlib]System.Threading.TimerCallback::.ctor(object,
                                                                                       native int)
    IL_0058:  ldnull
    IL_0059:  ldc.i4.s   20
    IL_005b:  ldc.i4.m1
    IL_005c:  newobj     instance void [mscorlib]System.Threading.Timer::.ctor(class [mscorlib]System.Threading.TimerCallback,
                                                                               object,
                                                                               int32,
                                                                               int32)
    IL_0061:  ldc.i4.s   60
    IL_0063:  call       void [mscorlib]System.Threading.Thread::Sleep(int32)
    IL_0068:  callvirt   instance void [mscorlib]System.Threading.Timer::Dispose()
    IL_006d:  ldstr      "after-oneshot: "
    IL_0072:  ldsflda    int32 Cases.TimerCase::g_fires
    IL_0077:  call       instance string [mscorlib]System.Int32::ToString()
    IL_007c:  call       string [mscorlib]System.String::Concat(string,
                                                                string)
    IL_0081:  call       void [mscorlib]System.Console::WriteLine(string)
    IL_0086:  ldnull
    IL_0087:  ldftn      void Cases.TimerCase::Cb(object)
    IL_008d:  newobj     instance void [mscorlib]System.Threading.TimerCallback::.ctor(object,
                                                                                       native int)
    IL_0092:  ldnull
    IL_0093:  ldc.i4.m1
    IL_0094:  ldc.i4.m1
    IL_0095:  newobj     instance void [mscorlib]System.Threading.Timer::.ctor(class [mscorlib]System.Threading.TimerCallback,
                                                                               object,
                                                                               int32,
                                                                               int32)
    IL_009a:  ldc.i4.s   40
    IL_009c:  call       void [mscorlib]System.Threading.Thread::Sleep(int32)
    IL_00a1:  ldstr      "after-unarmed: "
    IL_00a6:  ldsflda    int32 Cases.TimerCase::g_fires
    IL_00ab:  call       instance string [mscorlib]System.Int32::ToString()
    IL_00b0:  call       string [mscorlib]System.String::Concat(string,
                                                                string)
    IL_00b5:  call       void [mscorlib]System.Console::WriteLine(string)
    IL_00ba:  dup
    IL_00bb:  ldc.i4.s   10
    IL_00bd:  ldc.i4.m1
    IL_00be:  callvirt   instance bool [mscorlib]System.Threading.Timer::Change(int32,
                                                                                int32)
    IL_00c3:  pop
    IL_00c4:  ldc.i4.s   40
    IL_00c6:  call       void [mscorlib]System.Threading.Thread::Sleep(int32)
    IL_00cb:  callvirt   instance void [mscorlib]System.Threading.Timer::Dispose()
    IL_00d0:  ldstr      "after-change: "
    IL_00d5:  ldsflda    int32 Cases.TimerCase::g_fires
    IL_00da:  call       instance string [mscorlib]System.Int32::ToString()
    IL_00df:  call       string [mscorlib]System.String::Concat(string,
                                                                string)
    IL_00e4:  call       void [mscorlib]System.Console::WriteLine(string)
    IL_00e9:  ret
  } // end of method TimerCase::Main

} // end of class Cases.TimerCase


// =============================================================

// *********** 反汇编完成 ***********************
// 警告: 创建了 Win32 资源文件 tmp\timerprobe\timercase.res
