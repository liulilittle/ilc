
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0




// Metadata version: v4.0.30319
.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 4:0:0:0
}
.assembly asyncprobe
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
  .custom instance void [mscorlib]System.Reflection.AssemblyCompanyAttribute::.ctor(string) = ( 01 00 0A 61 73 79 6E 63 70 72 6F 62 65 00 00 )    // ...asyncprobe..
  .custom instance void [mscorlib]System.Reflection.AssemblyConfigurationAttribute::.ctor(string) = ( 01 00 07 52 65 6C 65 61 73 65 00 00 )             // ...Release..
  .custom instance void [mscorlib]System.Reflection.AssemblyFileVersionAttribute::.ctor(string) = ( 01 00 07 31 2E 30 2E 30 2E 30 00 00 )             // ...1.0.0.0..
  .custom instance void [mscorlib]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = ( 01 00 2E 31 2E 30 2E 30 2B 65 61 33 66 64 32 31   // ...1.0.0+ea3fd21
                                                                                                             37 37 36 62 62 64 37 64 64 35 62 37 38 35 66 61   // 776bbd7dd5b785fa
                                                                                                             36 32 64 39 63 34 33 38 30 31 66 30 38 34 34 63   // 62d9c43801f0844c
                                                                                                             36 00 00 )                                        // 6..
  .custom instance void [mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string) = ( 01 00 0A 61 73 79 6E 63 70 72 6F 62 65 00 00 )    // ...asyncprobe..
  .custom instance void [mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string) = ( 01 00 0A 61 73 79 6E 63 70 72 6F 62 65 00 00 )    // ...asyncprobe..
  .hash algorithm 0x00008004
  .ver 1:0:0:0
}
.module asyncprobe.exe
// MVID: {1E513983-C317-412B-9585-9274A2EA4791}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x05C40000


// =============== CLASS MEMBERS DECLARATION ===================

.class public abstract auto ansi sealed beforefieldinit Probe.AsyncProbe
       extends [mscorlib]System.Object
{
  .class auto ansi sealed nested private beforefieldinit '<F>d__1'
         extends [mscorlib]System.ValueType
         implements [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    .field public int32 '<>1__state'
    .field public valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> '<>t__builder'
    .field private int64 '<b>5__2'
    .field private valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> '<>u__1'
    .method private hidebysig newslot virtual final 
            instance void  MoveNext() cil managed
    {
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext
      // 代码大小       183 (0xb7)
      .maxstack  3
      .locals init (int32 V_0,
               int32 V_1,
               int32 V_2,
               valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> V_3,
               class [mscorlib]System.Exception V_4)
      IL_0000:  ldarg.0
      IL_0001:  ldfld      int32 Probe.AsyncProbe/'<F>d__1'::'<>1__state'
      IL_0006:  stloc.0
      .try
      {
        IL_0007:  ldloc.0
        IL_0008:  brfalse.s  IL_004a

        IL_000a:  ldc.i4.7
        IL_000b:  stloc.2
        IL_000c:  ldarg.0
        IL_000d:  ldc.i4.s   100
        IL_000f:  conv.i8
        IL_0010:  stfld      int64 Probe.AsyncProbe/'<F>d__1'::'<b>5__2'
        IL_0015:  ldloc.2
        IL_0016:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Probe.AsyncProbe::G(int32)
        IL_001b:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int32>::GetAwaiter()
        IL_0020:  stloc.3
        IL_0021:  ldloca.s   V_3
        IL_0023:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::get_IsCompleted()
        IL_0028:  brtrue.s   IL_0066

        IL_002a:  ldarg.0
        IL_002b:  ldc.i4.0
        IL_002c:  dup
        IL_002d:  stloc.0
        IL_002e:  stfld      int32 Probe.AsyncProbe/'<F>d__1'::'<>1__state'
        IL_0033:  ldarg.0
        IL_0034:  ldloc.3
        IL_0035:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>u__1'
        IL_003a:  ldarg.0
        IL_003b:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
        IL_0040:  ldloca.s   V_3
        IL_0042:  ldarg.0
        IL_0043:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>,valuetype Probe.AsyncProbe/'<F>d__1'>(!!0&,
                                                                                                                                                                                                                                                                      !!1&)
        IL_0048:  leave.s    IL_00b6

        IL_004a:  ldarg.0
        IL_004b:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>u__1'
        IL_0050:  stloc.3
        IL_0051:  ldarg.0
        IL_0052:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>u__1'
        IL_0057:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>
        IL_005d:  ldarg.0
        IL_005e:  ldc.i4.m1
        IL_005f:  dup
        IL_0060:  stloc.0
        IL_0061:  stfld      int32 Probe.AsyncProbe/'<F>d__1'::'<>1__state'
        IL_0066:  ldloca.s   V_3
        IL_0068:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::GetResult()
        IL_006d:  stloc.2
        IL_006e:  ldarg.0
        IL_006f:  ldarg.0
        IL_0070:  ldfld      int64 Probe.AsyncProbe/'<F>d__1'::'<b>5__2'
        IL_0075:  ldloc.2
        IL_0076:  conv.i8
        IL_0077:  add
        IL_0078:  stfld      int64 Probe.AsyncProbe/'<F>d__1'::'<b>5__2'
        IL_007d:  ldarg.0
        IL_007e:  ldfld      int64 Probe.AsyncProbe/'<F>d__1'::'<b>5__2'
        IL_0083:  conv.i4
        IL_0084:  ldloc.2
        IL_0085:  add
        IL_0086:  stloc.1
        IL_0087:  leave.s    IL_00a2

      }  // end .try
      catch [mscorlib]System.Exception 
      {
        IL_0089:  stloc.s    V_4
        IL_008b:  ldarg.0
        IL_008c:  ldc.i4.s   -2
        IL_008e:  stfld      int32 Probe.AsyncProbe/'<F>d__1'::'<>1__state'
        IL_0093:  ldarg.0
        IL_0094:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
        IL_0099:  ldloc.s    V_4
        IL_009b:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetException(class [mscorlib]System.Exception)
        IL_00a0:  leave.s    IL_00b6

      }  // end handler
      IL_00a2:  ldarg.0
      IL_00a3:  ldc.i4.s   -2
      IL_00a5:  stfld      int32 Probe.AsyncProbe/'<F>d__1'::'<>1__state'
      IL_00aa:  ldarg.0
      IL_00ab:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
      IL_00b0:  ldloc.1
      IL_00b1:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetResult(!0)
      IL_00b6:  ret
    } // end of method '<F>d__1'::MoveNext

    .method private hidebysig newslot virtual final 
            instance void  SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) cil managed
    {
      .custom instance void [mscorlib]System.Diagnostics.DebuggerHiddenAttribute::.ctor() = ( 01 00 00 00 ) 
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::SetStateMachine
      // 代码大小       13 (0xd)
      .maxstack  8
      IL_0000:  ldarg.0
      IL_0001:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
      IL_0006:  ldarg.1
      IL_0007:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine)
      IL_000c:  ret
    } // end of method '<F>d__1'::SetStateMachine

  } // end of class '<F>d__1'

  .method public hidebysig static void  Main() cil managed
  {
    .entrypoint
    // 代码大小       31 (0x1f)
    .maxstack  8
    IL_0000:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Probe.AsyncProbe::F()
    IL_0005:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_000a:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_000f:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Probe.AsyncProbe::F()
    IL_0014:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_0019:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_001e:  ret
  } // end of method AsyncProbe::Main

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int32> 
          F() cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.AsyncStateMachineAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 18 50 72 6F 62 65 2E 41 73 79 6E 63 50 72   // ...Probe.AsyncPr
                                                                                                                                       6F 62 65 2B 3C 46 3E 64 5F 5F 31 00 00 )          // obe+<F>d__1..
    // 代码大小       47 (0x2f)
    .maxstack  2
    .locals init (valuetype Probe.AsyncProbe/'<F>d__1' V_0)
    IL_0000:  ldloca.s   V_0
    IL_0002:  call       valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Create()
    IL_0007:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
    IL_000c:  ldloca.s   V_0
    IL_000e:  ldc.i4.m1
    IL_000f:  stfld      int32 Probe.AsyncProbe/'<F>d__1'::'<>1__state'
    IL_0014:  ldloca.s   V_0
    IL_0016:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
    IL_001b:  ldloca.s   V_0
    IL_001d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Start<valuetype Probe.AsyncProbe/'<F>d__1'>(!!0&)
    IL_0022:  ldloca.s   V_0
    IL_0024:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Probe.AsyncProbe/'<F>d__1'::'<>t__builder'
    IL_0029:  call       instance class [mscorlib]System.Threading.Tasks.Task`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::get_Task()
    IL_002e:  ret
  } // end of method AsyncProbe::F

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int32> 
          G(int32 x) cil managed
  {
    // 代码大小       9 (0x9)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldc.i4.1
    IL_0002:  add
    IL_0003:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
    IL_0008:  ret
  } // end of method AsyncProbe::G

} // end of class Probe.AsyncProbe


// =============================================================

// *********** 反汇编完成 ***********************
// 警告: 创建了 Win32 资源文件 tmp\asyncprobe\dump.res
