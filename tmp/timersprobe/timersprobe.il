
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0




// Metadata version: v4.0.30319
.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 4:0:0:0
}
.assembly extern System
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 4:0:0:0
}
.assembly timersprobe
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
  .custom instance void [mscorlib]System.Reflection.AssemblyCompanyAttribute::.ctor(string) = ( 01 00 0B 74 69 6D 65 72 73 70 72 6F 62 65 00 00 ) // ...timersprobe..
  .custom instance void [mscorlib]System.Reflection.AssemblyConfigurationAttribute::.ctor(string) = ( 01 00 07 52 65 6C 65 61 73 65 00 00 )             // ...Release..
  .custom instance void [mscorlib]System.Reflection.AssemblyFileVersionAttribute::.ctor(string) = ( 01 00 07 31 2E 30 2E 30 2E 30 00 00 )             // ...1.0.0.0..
  .custom instance void [mscorlib]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = ( 01 00 2E 31 2E 30 2E 30 2B 30 36 32 33 61 66 31   // ...1.0.0+0623af1
                                                                                                             30 66 31 66 62 33 64 33 39 31 32 38 32 64 61 64   // 0f1fb3d391282dad
                                                                                                             64 63 62 34 34 66 65 35 38 33 35 62 61 33 39 31   // dcb44fe5835ba391
                                                                                                             64 00 00 )                                        // d..
  .custom instance void [mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string) = ( 01 00 0B 74 69 6D 65 72 73 70 72 6F 62 65 00 00 ) // ...timersprobe..
  .custom instance void [mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string) = ( 01 00 0B 74 69 6D 65 72 73 70 72 6F 62 65 00 00 ) // ...timersprobe..
  .hash algorithm 0x00008004
  .ver 1:0:0:0
}
.module timersprobe.exe
// MVID: {8B15868E-4F70-4A7B-946C-98757062681B}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x05B00000


// =============== CLASS MEMBERS DECLARATION ===================

.class public abstract auto ansi sealed beforefieldinit Probe.TimersProbe
       extends [mscorlib]System.Object
{
  .field private static int32 g_fires
  .method private hidebysig static void  OnElapsed(object sender,
                                                   class [System]System.Timers.ElapsedEventArgs e) cil managed
  {
    // 代码大小       13 (0xd)
    .maxstack  8
    IL_0000:  ldsfld     int32 Probe.TimersProbe::g_fires
    IL_0005:  ldc.i4.1
    IL_0006:  add
    IL_0007:  stsfld     int32 Probe.TimersProbe::g_fires
    IL_000c:  ret
  } // end of method TimersProbe::OnElapsed

  .method public hidebysig static void  Main() cil managed
  {
    .entrypoint
    // 代码大小       186 (0xba)
    .maxstack  4
    .locals init (class [System]System.Timers.Timer V_0)
    IL_0000:  ldc.r8     50.
    IL_0009:  newobj     instance void [System]System.Timers.Timer::.ctor(float64)
    IL_000e:  stloc.0
    .try
    {
      IL_000f:  ldloc.0
      IL_0010:  ldnull
      IL_0011:  ldftn      void Probe.TimersProbe::OnElapsed(object,
                                                             class [System]System.Timers.ElapsedEventArgs)
      IL_0017:  newobj     instance void [System]System.Timers.ElapsedEventHandler::.ctor(object,
                                                                                          native int)
      IL_001c:  callvirt   instance void [System]System.Timers.Timer::add_Elapsed(class [System]System.Timers.ElapsedEventHandler)
      IL_0021:  ldloc.0
      IL_0022:  ldc.i4.0
      IL_0023:  callvirt   instance void [System]System.Timers.Timer::set_AutoReset(bool)
      IL_0028:  ldloc.0
      IL_0029:  callvirt   instance void [System]System.Timers.Timer::Start()
      IL_002e:  ldc.i4.s   120
      IL_0030:  call       void [mscorlib]System.Threading.Thread::Sleep(int32)
      IL_0035:  ldloc.0
      IL_0036:  callvirt   instance void [System]System.Timers.Timer::Stop()
      IL_003b:  leave.s    IL_0047

    }  // end .try
    finally
    {
      IL_003d:  ldloc.0
      IL_003e:  brfalse.s  IL_0046

      IL_0040:  ldloc.0
      IL_0041:  callvirt   instance void [mscorlib]System.IDisposable::Dispose()
      IL_0046:  endfinally
    }  // end handler
    IL_0047:  ldsfld     int32 Probe.TimersProbe::g_fires
    IL_004c:  ldc.i4.1
    IL_004d:  ceq
    IL_004f:  call       void [mscorlib]System.Console::WriteLine(bool)
    IL_0054:  newobj     instance void [System]System.Timers.Timer::.ctor()
    IL_0059:  dup
    IL_005a:  ldc.r8     30.
    IL_0063:  callvirt   instance void [System]System.Timers.Timer::set_Interval(float64)
    IL_0068:  dup
    IL_0069:  ldc.i4.1
    IL_006a:  callvirt   instance void [System]System.Timers.Timer::set_AutoReset(bool)
    IL_006f:  dup
    IL_0070:  ldnull
    IL_0071:  ldftn      void Probe.TimersProbe::OnElapsed(object,
                                                           class [System]System.Timers.ElapsedEventArgs)
    IL_0077:  newobj     instance void [System]System.Timers.ElapsedEventHandler::.ctor(object,
                                                                                        native int)
    IL_007c:  callvirt   instance void [System]System.Timers.Timer::add_Elapsed(class [System]System.Timers.ElapsedEventHandler)
    IL_0081:  dup
    IL_0082:  callvirt   instance void [System]System.Timers.Timer::Start()
    IL_0087:  ldc.i4.s   100
    IL_0089:  call       void [mscorlib]System.Threading.Thread::Sleep(int32)
    IL_008e:  dup
    IL_008f:  callvirt   instance void [System]System.Timers.Timer::Stop()
    IL_0094:  callvirt   instance void [System]System.ComponentModel.Component::Dispose()
    IL_0099:  ldsfld     int32 Probe.TimersProbe::g_fires
    IL_009e:  ldc.i4.2
    IL_009f:  clt
    IL_00a1:  ldc.i4.0
    IL_00a2:  ceq
    IL_00a4:  call       void [mscorlib]System.Console::WriteLine(bool)
    IL_00a9:  ldsfld     int32 Probe.TimersProbe::g_fires
    IL_00ae:  ldc.i4.5
    IL_00af:  cgt
    IL_00b1:  ldc.i4.0
    IL_00b2:  ceq
    IL_00b4:  call       void [mscorlib]System.Console::WriteLine(bool)
    IL_00b9:  ret
  } // end of method TimersProbe::Main

} // end of class Probe.TimersProbe


// =============================================================

// *********** 反汇编完成 ***********************
// 警告: 创建了 Win32 资源文件 tmp\timersprobe\timersprobe.res
