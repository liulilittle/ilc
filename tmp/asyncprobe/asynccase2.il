
//  Microsoft (R) .NET Framework IL Disassembler.  Version 4.8.3928.0




// Metadata version: v4.0.30319
.assembly extern mscorlib
{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89 )                         // .z\V.4..
  .ver 4:0:0:0
}
.assembly asynccase
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
  .custom instance void [mscorlib]System.Reflection.AssemblyCompanyAttribute::.ctor(string) = ( 01 00 09 61 73 79 6E 63 63 61 73 65 00 00 )       // ...asynccase..
  .custom instance void [mscorlib]System.Reflection.AssemblyConfigurationAttribute::.ctor(string) = ( 01 00 07 52 65 6C 65 61 73 65 00 00 )             // ...Release..
  .custom instance void [mscorlib]System.Reflection.AssemblyFileVersionAttribute::.ctor(string) = ( 01 00 07 31 2E 30 2E 30 2E 30 00 00 )             // ...1.0.0.0..
  .custom instance void [mscorlib]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = ( 01 00 2E 31 2E 30 2E 30 2B 63 61 30 34 63 61 36   // ...1.0.0+ca04ca6
                                                                                                             37 33 36 37 66 39 35 64 31 61 37 64 61 65 32 64   // 7367f95d1a7dae2d
                                                                                                             35 33 63 65 63 64 61 31 34 61 66 37 35 63 65 34   // 53cecda14af75ce4
                                                                                                             37 00 00 )                                        // 7..
  .custom instance void [mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string) = ( 01 00 09 61 73 79 6E 63 63 61 73 65 00 00 )       // ...asynccase..
  .custom instance void [mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string) = ( 01 00 09 61 73 79 6E 63 63 61 73 65 00 00 )       // ...asynccase..
  .hash algorithm 0x00008004
  .ver 1:0:0:0
}
.module asynccase.exe
// MVID: {09652ECF-8709-4306-B286-9D5E0492F643}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x058E0000


// =============== CLASS MEMBERS DECLARATION ===================

.class public abstract auto ansi sealed beforefieldinit Cases.AsyncCase
       extends [mscorlib]System.Object
{
  .class auto ansi sealed nested private beforefieldinit '<F>d__1'
         extends [mscorlib]System.ValueType
         implements [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    .field public int32 '<>1__state'
    .field public valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> '<>t__builder'
    .field private valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> '<>u__1'
    .method private hidebysig newslot virtual final 
            instance void  MoveNext() cil managed
    {
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext
      // 代码大小       147 (0x93)
      .maxstack  3
      .locals init (int32 V_0,
               int32 V_1,
               valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> V_2,
               class [mscorlib]System.Exception V_3)
      IL_0000:  ldarg.0
      IL_0001:  ldfld      int32 Cases.AsyncCase/'<F>d__1'::'<>1__state'
      IL_0006:  stloc.0
      .try
      {
        IL_0007:  ldloc.0
        IL_0008:  brfalse.s  IL_003f

        IL_000a:  ldc.i4.7
        IL_000b:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::G(int32)
        IL_0010:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int32>::GetAwaiter()
        IL_0015:  stloc.2
        IL_0016:  ldloca.s   V_2
        IL_0018:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::get_IsCompleted()
        IL_001d:  brtrue.s   IL_005b

        IL_001f:  ldarg.0
        IL_0020:  ldc.i4.0
        IL_0021:  dup
        IL_0022:  stloc.0
        IL_0023:  stfld      int32 Cases.AsyncCase/'<F>d__1'::'<>1__state'
        IL_0028:  ldarg.0
        IL_0029:  ldloc.2
        IL_002a:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<F>d__1'::'<>u__1'
        IL_002f:  ldarg.0
        IL_0030:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
        IL_0035:  ldloca.s   V_2
        IL_0037:  ldarg.0
        IL_0038:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>,valuetype Cases.AsyncCase/'<F>d__1'>(!!0&,
                                                                                                                                                                                                                                                                     !!1&)
        IL_003d:  leave.s    IL_0092

        IL_003f:  ldarg.0
        IL_0040:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<F>d__1'::'<>u__1'
        IL_0045:  stloc.2
        IL_0046:  ldarg.0
        IL_0047:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<F>d__1'::'<>u__1'
        IL_004c:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>
        IL_0052:  ldarg.0
        IL_0053:  ldc.i4.m1
        IL_0054:  dup
        IL_0055:  stloc.0
        IL_0056:  stfld      int32 Cases.AsyncCase/'<F>d__1'::'<>1__state'
        IL_005b:  ldloca.s   V_2
        IL_005d:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::GetResult()
        IL_0062:  ldc.i4.1
        IL_0063:  add
        IL_0064:  stloc.1
        IL_0065:  leave.s    IL_007e

      }  // end .try
      catch [mscorlib]System.Exception 
      {
        IL_0067:  stloc.3
        IL_0068:  ldarg.0
        IL_0069:  ldc.i4.s   -2
        IL_006b:  stfld      int32 Cases.AsyncCase/'<F>d__1'::'<>1__state'
        IL_0070:  ldarg.0
        IL_0071:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
        IL_0076:  ldloc.3
        IL_0077:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetException(class [mscorlib]System.Exception)
        IL_007c:  leave.s    IL_0092

      }  // end handler
      IL_007e:  ldarg.0
      IL_007f:  ldc.i4.s   -2
      IL_0081:  stfld      int32 Cases.AsyncCase/'<F>d__1'::'<>1__state'
      IL_0086:  ldarg.0
      IL_0087:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
      IL_008c:  ldloc.1
      IL_008d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetResult(!0)
      IL_0092:  ret
    } // end of method '<F>d__1'::MoveNext

    .method private hidebysig newslot virtual final 
            instance void  SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) cil managed
    {
      .custom instance void [mscorlib]System.Diagnostics.DebuggerHiddenAttribute::.ctor() = ( 01 00 00 00 ) 
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::SetStateMachine
      // 代码大小       13 (0xd)
      .maxstack  8
      IL_0000:  ldarg.0
      IL_0001:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
      IL_0006:  ldarg.1
      IL_0007:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine)
      IL_000c:  ret
    } // end of method '<F>d__1'::SetStateMachine

  } // end of class '<F>d__1'

  .class auto ansi sealed nested private beforefieldinit '<H>d__3'
         extends [mscorlib]System.ValueType
         implements [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    .field public int32 '<>1__state'
    .field public valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> '<>t__builder'
    .field private valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> '<>u__1'
    .method private hidebysig newslot virtual final 
            instance void  MoveNext() cil managed
    {
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext
      // 代码大小       251 (0xfb)
      .maxstack  3
      .locals init (int32 V_0,
               int32 V_1,
               valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> V_2,
               class [mscorlib]System.Exception V_3)
      IL_0000:  ldarg.0
      IL_0001:  ldfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
      IL_0006:  stloc.0
      .try
      {
        IL_0007:  ldloc.0
        IL_0008:  brfalse.s  IL_004b

        IL_000a:  ldloc.0
        IL_000b:  ldc.i4.1
        IL_000c:  beq        IL_00a9

        IL_0011:  ldc.i4.3
        IL_0012:  ldc.i4.2
        IL_0013:  mul
        IL_0014:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
        IL_0019:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int32>::GetAwaiter()
        IL_001e:  stloc.2
        IL_001f:  ldloca.s   V_2
        IL_0021:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::get_IsCompleted()
        IL_0026:  brtrue.s   IL_0067

        IL_0028:  ldarg.0
        IL_0029:  ldc.i4.0
        IL_002a:  dup
        IL_002b:  stloc.0
        IL_002c:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_0031:  ldarg.0
        IL_0032:  ldloc.2
        IL_0033:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_0038:  ldarg.0
        IL_0039:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
        IL_003e:  ldloca.s   V_2
        IL_0040:  ldarg.0
        IL_0041:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>,valuetype Cases.AsyncCase/'<H>d__3'>(!!0&,
                                                                                                                                                                                                                                                                     !!1&)
        IL_0046:  leave      IL_00fa

        IL_004b:  ldarg.0
        IL_004c:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_0051:  stloc.2
        IL_0052:  ldarg.0
        IL_0053:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_0058:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>
        IL_005e:  ldarg.0
        IL_005f:  ldc.i4.m1
        IL_0060:  dup
        IL_0061:  stloc.0
        IL_0062:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_0067:  ldloca.s   V_2
        IL_0069:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::GetResult()
        IL_006e:  call       void [mscorlib]System.GC::Collect()
        IL_0073:  ldc.i4.4
        IL_0074:  add
        IL_0075:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
        IL_007a:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int32>::GetAwaiter()
        IL_007f:  stloc.2
        IL_0080:  ldloca.s   V_2
        IL_0082:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::get_IsCompleted()
        IL_0087:  brtrue.s   IL_00c5

        IL_0089:  ldarg.0
        IL_008a:  ldc.i4.1
        IL_008b:  dup
        IL_008c:  stloc.0
        IL_008d:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_0092:  ldarg.0
        IL_0093:  ldloc.2
        IL_0094:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_0099:  ldarg.0
        IL_009a:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
        IL_009f:  ldloca.s   V_2
        IL_00a1:  ldarg.0
        IL_00a2:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>,valuetype Cases.AsyncCase/'<H>d__3'>(!!0&,
                                                                                                                                                                                                                                                                     !!1&)
        IL_00a7:  leave.s    IL_00fa

        IL_00a9:  ldarg.0
        IL_00aa:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_00af:  stloc.2
        IL_00b0:  ldarg.0
        IL_00b1:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_00b6:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>
        IL_00bc:  ldarg.0
        IL_00bd:  ldc.i4.m1
        IL_00be:  dup
        IL_00bf:  stloc.0
        IL_00c0:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_00c5:  ldloca.s   V_2
        IL_00c7:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::GetResult()
        IL_00cc:  stloc.1
        IL_00cd:  leave.s    IL_00e6

      }  // end .try
      catch [mscorlib]System.Exception 
      {
        IL_00cf:  stloc.3
        IL_00d0:  ldarg.0
        IL_00d1:  ldc.i4.s   -2
        IL_00d3:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_00d8:  ldarg.0
        IL_00d9:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
        IL_00de:  ldloc.3
        IL_00df:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetException(class [mscorlib]System.Exception)
        IL_00e4:  leave.s    IL_00fa

      }  // end handler
      IL_00e6:  ldarg.0
      IL_00e7:  ldc.i4.s   -2
      IL_00e9:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
      IL_00ee:  ldarg.0
      IL_00ef:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
      IL_00f4:  ldloc.1
      IL_00f5:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetResult(!0)
      IL_00fa:  ret
    } // end of method '<H>d__3'::MoveNext

    .method private hidebysig newslot virtual final 
            instance void  SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) cil managed
    {
      .custom instance void [mscorlib]System.Diagnostics.DebuggerHiddenAttribute::.ctor() = ( 01 00 00 00 ) 
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::SetStateMachine
      // 代码大小       13 (0xd)
      .maxstack  8
      IL_0000:  ldarg.0
      IL_0001:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
      IL_0006:  ldarg.1
      IL_0007:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine)
      IL_000c:  ret
    } // end of method '<H>d__3'::SetStateMachine

  } // end of class '<H>d__3'

  .class auto ansi sealed nested private beforefieldinit '<L>d__5'
         extends [mscorlib]System.ValueType
         implements [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    .field public int32 '<>1__state'
    .field public valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> '<>t__builder'
    .field private valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64> '<>u__1'
    .method private hidebysig newslot virtual final 
            instance void  MoveNext() cil managed
    {
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext
      // 代码大小       153 (0x99)
      .maxstack  3
      .locals init (int32 V_0,
               int64 V_1,
               valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64> V_2,
               class [mscorlib]System.Exception V_3)
      IL_0000:  ldarg.0
      IL_0001:  ldfld      int32 Cases.AsyncCase/'<L>d__5'::'<>1__state'
      IL_0006:  stloc.0
      .try
      {
        IL_0007:  ldloc.0
        IL_0008:  brfalse.s  IL_0044

        IL_000a:  ldc.i4.s   100
        IL_000c:  conv.i8
        IL_000d:  ldc.i4.5
        IL_000e:  conv.i8
        IL_000f:  add
        IL_0010:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int64>(!!0)
        IL_0015:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int64>::GetAwaiter()
        IL_001a:  stloc.2
        IL_001b:  ldloca.s   V_2
        IL_001d:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64>::get_IsCompleted()
        IL_0022:  brtrue.s   IL_0060

        IL_0024:  ldarg.0
        IL_0025:  ldc.i4.0
        IL_0026:  dup
        IL_0027:  stloc.0
        IL_0028:  stfld      int32 Cases.AsyncCase/'<L>d__5'::'<>1__state'
        IL_002d:  ldarg.0
        IL_002e:  ldloc.2
        IL_002f:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64> Cases.AsyncCase/'<L>d__5'::'<>u__1'
        IL_0034:  ldarg.0
        IL_0035:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
        IL_003a:  ldloca.s   V_2
        IL_003c:  ldarg.0
        IL_003d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64>,valuetype Cases.AsyncCase/'<L>d__5'>(!!0&,
                                                                                                                                                                                                                                                                     !!1&)
        IL_0042:  leave.s    IL_0098

        IL_0044:  ldarg.0
        IL_0045:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64> Cases.AsyncCase/'<L>d__5'::'<>u__1'
        IL_004a:  stloc.2
        IL_004b:  ldarg.0
        IL_004c:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64> Cases.AsyncCase/'<L>d__5'::'<>u__1'
        IL_0051:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64>
        IL_0057:  ldarg.0
        IL_0058:  ldc.i4.m1
        IL_0059:  dup
        IL_005a:  stloc.0
        IL_005b:  stfld      int32 Cases.AsyncCase/'<L>d__5'::'<>1__state'
        IL_0060:  ldloca.s   V_2
        IL_0062:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int64>::GetResult()
        IL_0067:  ldc.i4.2
        IL_0068:  conv.i8
        IL_0069:  mul
        IL_006a:  stloc.1
        IL_006b:  leave.s    IL_0084

      }  // end .try
      catch [mscorlib]System.Exception 
      {
        IL_006d:  stloc.3
        IL_006e:  ldarg.0
        IL_006f:  ldc.i4.s   -2
        IL_0071:  stfld      int32 Cases.AsyncCase/'<L>d__5'::'<>1__state'
        IL_0076:  ldarg.0
        IL_0077:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
        IL_007c:  ldloc.3
        IL_007d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::SetException(class [mscorlib]System.Exception)
        IL_0082:  leave.s    IL_0098

      }  // end handler
      IL_0084:  ldarg.0
      IL_0085:  ldc.i4.s   -2
      IL_0087:  stfld      int32 Cases.AsyncCase/'<L>d__5'::'<>1__state'
      IL_008c:  ldarg.0
      IL_008d:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
      IL_0092:  ldloc.1
      IL_0093:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::SetResult(!0)
      IL_0098:  ret
    } // end of method '<L>d__5'::MoveNext

    .method private hidebysig newslot virtual final 
            instance void  SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) cil managed
    {
      .custom instance void [mscorlib]System.Diagnostics.DebuggerHiddenAttribute::.ctor() = ( 01 00 00 00 ) 
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::SetStateMachine
      // 代码大小       13 (0xd)
      .maxstack  8
      IL_0000:  ldarg.0
      IL_0001:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
      IL_0006:  ldarg.1
      IL_0007:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine)
      IL_000c:  ret
    } // end of method '<L>d__5'::SetStateMachine

  } // end of class '<L>d__5'

  .class auto ansi sealed nested private beforefieldinit '<T>d__4'
         extends [mscorlib]System.ValueType
         implements [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) 
    .field public int32 '<>1__state'
    .field public valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> '<>t__builder'
    .field private valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> '<>u__1'
    .method private hidebysig newslot virtual final 
            instance void  MoveNext() cil managed
    {
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::MoveNext
      // 代码大小       162 (0xa2)
      .maxstack  3
      .locals init (int32 V_0,
               int32 V_1,
               valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> V_2,
               class [mscorlib]System.Exception V_3)
      IL_0000:  ldarg.0
      IL_0001:  ldfld      int32 Cases.AsyncCase/'<T>d__4'::'<>1__state'
      IL_0006:  stloc.0
      .try
      {
        IL_0007:  ldloc.0
        IL_0008:  brfalse.s  IL_0041

        IL_000a:  ldc.i4.1
        IL_000b:  ldc.i4.1
        IL_000c:  add
        IL_000d:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
        IL_0012:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int32>::GetAwaiter()
        IL_0017:  stloc.2
        IL_0018:  ldloca.s   V_2
        IL_001a:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::get_IsCompleted()
        IL_001f:  brtrue.s   IL_005d

        IL_0021:  ldarg.0
        IL_0022:  ldc.i4.0
        IL_0023:  dup
        IL_0024:  stloc.0
        IL_0025:  stfld      int32 Cases.AsyncCase/'<T>d__4'::'<>1__state'
        IL_002a:  ldarg.0
        IL_002b:  ldloc.2
        IL_002c:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<T>d__4'::'<>u__1'
        IL_0031:  ldarg.0
        IL_0032:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
        IL_0037:  ldloca.s   V_2
        IL_0039:  ldarg.0
        IL_003a:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>,valuetype Cases.AsyncCase/'<T>d__4'>(!!0&,
                                                                                                                                                                                                                                                                     !!1&)
        IL_003f:  leave.s    IL_00a1

        IL_0041:  ldarg.0
        IL_0042:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<T>d__4'::'<>u__1'
        IL_0047:  stloc.2
        IL_0048:  ldarg.0
        IL_0049:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<T>d__4'::'<>u__1'
        IL_004e:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>
        IL_0054:  ldarg.0
        IL_0055:  ldc.i4.m1
        IL_0056:  dup
        IL_0057:  stloc.0
        IL_0058:  stfld      int32 Cases.AsyncCase/'<T>d__4'::'<>1__state'
        IL_005d:  ldloca.s   V_2
        IL_005f:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::GetResult()
        IL_0064:  dup
        IL_0065:  ldc.i4.2
        IL_0066:  bne.un.s   IL_0073

        IL_0068:  ldstr      "async boom"
        IL_006d:  newobj     instance void [mscorlib]System.Exception::.ctor(string)
        IL_0072:  throw

        IL_0073:  stloc.1
        IL_0074:  leave.s    IL_008d

      }  // end .try
      catch [mscorlib]System.Exception 
      {
        IL_0076:  stloc.3
        IL_0077:  ldarg.0
        IL_0078:  ldc.i4.s   -2
        IL_007a:  stfld      int32 Cases.AsyncCase/'<T>d__4'::'<>1__state'
        IL_007f:  ldarg.0
        IL_0080:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
        IL_0085:  ldloc.3
        IL_0086:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetException(class [mscorlib]System.Exception)
        IL_008b:  leave.s    IL_00a1

      }  // end handler
      IL_008d:  ldarg.0
      IL_008e:  ldc.i4.s   -2
      IL_0090:  stfld      int32 Cases.AsyncCase/'<T>d__4'::'<>1__state'
      IL_0095:  ldarg.0
      IL_0096:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
      IL_009b:  ldloc.1
      IL_009c:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetResult(!0)
      IL_00a1:  ret
    } // end of method '<T>d__4'::MoveNext

    .method private hidebysig newslot virtual final 
            instance void  SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) cil managed
    {
      .custom instance void [mscorlib]System.Diagnostics.DebuggerHiddenAttribute::.ctor() = ( 01 00 00 00 ) 
      .override [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine::SetStateMachine
      // 代码大小       13 (0xd)
      .maxstack  8
      IL_0000:  ldarg.0
      IL_0001:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
      IL_0006:  ldarg.1
      IL_0007:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetStateMachine(class [mscorlib]System.Runtime.CompilerServices.IAsyncStateMachine)
      IL_000c:  ret
    } // end of method '<T>d__4'::SetStateMachine

  } // end of class '<T>d__4'

  .method public hidebysig static void  Main() cil managed
  {
    .entrypoint
    // 代码大小       267 (0x10b)
    .maxstack  2
    .locals init (int32 V_0,
             class [mscorlib]System.Threading.Tasks.Task`1<int32> V_1,
             class [mscorlib]System.Threading.Tasks.Task`1<int32> V_2,
             int32 V_3,
             int32 V_4,
             int32 V_5)
    IL_0000:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::F()
    IL_0005:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_000a:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_000f:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::F()
    IL_0014:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_0019:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_001e:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::H()
    IL_0023:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_0028:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_002d:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::H()
    IL_0032:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_0037:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_003c:  ldc.i4.0
    IL_003d:  stloc.0
    IL_003e:  ldc.i4.0
    IL_003f:  stloc.3
    IL_0040:  br.s       IL_0053

    IL_0042:  ldloc.0
    IL_0043:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::F()
    IL_0048:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_004d:  add
    IL_004e:  stloc.0
    IL_004f:  ldloc.3
    IL_0050:  ldc.i4.1
    IL_0051:  add
    IL_0052:  stloc.3
    IL_0053:  ldloc.3
    IL_0054:  ldc.i4     0xc8
    IL_0059:  blt.s      IL_0042

    IL_005b:  ldloc.0
    IL_005c:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_0061:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::H()
    IL_0066:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
    IL_006b:  call       void [mscorlib]System.Console::WriteLine(int32)
    IL_0070:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::T()
    IL_0075:  stloc.1
    IL_0076:  ldloc.1
    IL_0077:  callvirt   instance bool [mscorlib]System.Threading.Tasks.Task::get_IsCompleted()
    IL_007c:  call       void [mscorlib]System.Console::WriteLine(bool)
    .try
    {
      IL_0081:  ldloc.1
      IL_0082:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
      IL_0087:  stloc.s    V_4
      IL_0089:  ldstr      "no-throw:"
      IL_008e:  ldloca.s   V_4
      IL_0090:  call       instance string [mscorlib]System.Int32::ToString()
      IL_0095:  call       string [mscorlib]System.String::Concat(string,
                                                                  string)
      IL_009a:  call       void [mscorlib]System.Console::WriteLine(string)
      IL_009f:  leave.s    IL_00ae

    }  // end .try
    catch [mscorlib]System.Exception 
    {
      IL_00a1:  pop
      IL_00a2:  ldstr      "caught"
      IL_00a7:  call       void [mscorlib]System.Console::WriteLine(string)
      IL_00ac:  leave.s    IL_00ae

    }  // end handler
    IL_00ae:  call       class [mscorlib]System.Threading.Tasks.Task`1<int32> Cases.AsyncCase::T()
    IL_00b3:  stloc.2
    IL_00b4:  ldloc.2
    IL_00b5:  callvirt   instance bool [mscorlib]System.Threading.Tasks.Task::get_IsCompleted()
    IL_00ba:  call       void [mscorlib]System.Console::WriteLine(bool)
    .try
    {
      IL_00bf:  ldloc.2
      IL_00c0:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int32>::get_Result()
      IL_00c5:  stloc.s    V_5
      IL_00c7:  ldstr      "no-throw:"
      IL_00cc:  ldloca.s   V_5
      IL_00ce:  call       instance string [mscorlib]System.Int32::ToString()
      IL_00d3:  call       string [mscorlib]System.String::Concat(string,
                                                                  string)
      IL_00d8:  call       void [mscorlib]System.Console::WriteLine(string)
      IL_00dd:  leave.s    IL_00ec

    }  // end .try
    catch [mscorlib]System.Exception 
    {
      IL_00df:  pop
      IL_00e0:  ldstr      "caught"
      IL_00e5:  call       void [mscorlib]System.Console::WriteLine(string)
      IL_00ea:  leave.s    IL_00ec

    }  // end handler
    IL_00ec:  call       class [mscorlib]System.Threading.Tasks.Task`1<int64> Cases.AsyncCase::L()
    IL_00f1:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int64>::get_Result()
    IL_00f6:  call       void [mscorlib]System.Console::WriteLine(int64)
    IL_00fb:  call       class [mscorlib]System.Threading.Tasks.Task`1<int64> Cases.AsyncCase::L()
    IL_0100:  callvirt   instance !0 class [mscorlib]System.Threading.Tasks.Task`1<int64>::get_Result()
    IL_0105:  call       void [mscorlib]System.Console::WriteLine(int64)
    IL_010a:  ret
  } // end of method AsyncCase::Main

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int32> 
          F() cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.AsyncStateMachineAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 17 43 61 73 65 73 2E 41 73 79 6E 63 43 61   // ...Cases.AsyncCa
                                                                                                                                       73 65 2B 3C 46 3E 64 5F 5F 31 00 00 )             // se+<F>d__1..
    // 代码大小       47 (0x2f)
    .maxstack  2
    .locals init (valuetype Cases.AsyncCase/'<F>d__1' V_0)
    IL_0000:  ldloca.s   V_0
    IL_0002:  call       valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Create()
    IL_0007:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
    IL_000c:  ldloca.s   V_0
    IL_000e:  ldc.i4.m1
    IL_000f:  stfld      int32 Cases.AsyncCase/'<F>d__1'::'<>1__state'
    IL_0014:  ldloca.s   V_0
    IL_0016:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
    IL_001b:  ldloca.s   V_0
    IL_001d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Start<valuetype Cases.AsyncCase/'<F>d__1'>(!!0&)
    IL_0022:  ldloca.s   V_0
    IL_0024:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<F>d__1'::'<>t__builder'
    IL_0029:  call       instance class [mscorlib]System.Threading.Tasks.Task`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::get_Task()
    IL_002e:  ret
  } // end of method AsyncCase::F

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int32> 
          G(int32 x) cil managed
  {
    // 代码大小       14 (0xe)
    .maxstack  8
    IL_0000:  call       void [mscorlib]System.GC::Collect()
    IL_0005:  ldarg.0
    IL_0006:  ldc.i4.1
    IL_0007:  add
    IL_0008:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
    IL_000d:  ret
  } // end of method AsyncCase::G

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int32> 
          H() cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.AsyncStateMachineAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 17 43 61 73 65 73 2E 41 73 79 6E 63 43 61   // ...Cases.AsyncCa
                                                                                                                                       73 65 2B 3C 48 3E 64 5F 5F 33 00 00 )             // se+<H>d__3..
    // 代码大小       47 (0x2f)
    .maxstack  2
    .locals init (valuetype Cases.AsyncCase/'<H>d__3' V_0)
    IL_0000:  ldloca.s   V_0
    IL_0002:  call       valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Create()
    IL_0007:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
    IL_000c:  ldloca.s   V_0
    IL_000e:  ldc.i4.m1
    IL_000f:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
    IL_0014:  ldloca.s   V_0
    IL_0016:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
    IL_001b:  ldloca.s   V_0
    IL_001d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Start<valuetype Cases.AsyncCase/'<H>d__3'>(!!0&)
    IL_0022:  ldloca.s   V_0
    IL_0024:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
    IL_0029:  call       instance class [mscorlib]System.Threading.Tasks.Task`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::get_Task()
    IL_002e:  ret
  } // end of method AsyncCase::H

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int32> 
          T() cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.AsyncStateMachineAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 17 43 61 73 65 73 2E 41 73 79 6E 63 43 61   // ...Cases.AsyncCa
                                                                                                                                       73 65 2B 3C 54 3E 64 5F 5F 34 00 00 )             // se+<T>d__4..
    // 代码大小       47 (0x2f)
    .maxstack  2
    .locals init (valuetype Cases.AsyncCase/'<T>d__4' V_0)
    IL_0000:  ldloca.s   V_0
    IL_0002:  call       valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Create()
    IL_0007:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
    IL_000c:  ldloca.s   V_0
    IL_000e:  ldc.i4.m1
    IL_000f:  stfld      int32 Cases.AsyncCase/'<T>d__4'::'<>1__state'
    IL_0014:  ldloca.s   V_0
    IL_0016:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
    IL_001b:  ldloca.s   V_0
    IL_001d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::Start<valuetype Cases.AsyncCase/'<T>d__4'>(!!0&)
    IL_0022:  ldloca.s   V_0
    IL_0024:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<T>d__4'::'<>t__builder'
    IL_0029:  call       instance class [mscorlib]System.Threading.Tasks.Task`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::get_Task()
    IL_002e:  ret
  } // end of method AsyncCase::T

  .method private hidebysig static class [mscorlib]System.Threading.Tasks.Task`1<int64> 
          L() cil managed
  {
    .custom instance void [mscorlib]System.Runtime.CompilerServices.AsyncStateMachineAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 17 43 61 73 65 73 2E 41 73 79 6E 63 43 61   // ...Cases.AsyncCa
                                                                                                                                       73 65 2B 3C 4C 3E 64 5F 5F 35 00 00 )             // se+<L>d__5..
    // 代码大小       47 (0x2f)
    .maxstack  2
    .locals init (valuetype Cases.AsyncCase/'<L>d__5' V_0)
    IL_0000:  ldloca.s   V_0
    IL_0002:  call       valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::Create()
    IL_0007:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
    IL_000c:  ldloca.s   V_0
    IL_000e:  ldc.i4.m1
    IL_000f:  stfld      int32 Cases.AsyncCase/'<L>d__5'::'<>1__state'
    IL_0014:  ldloca.s   V_0
    IL_0016:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
    IL_001b:  ldloca.s   V_0
    IL_001d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::Start<valuetype Cases.AsyncCase/'<L>d__5'>(!!0&)
    IL_0022:  ldloca.s   V_0
    IL_0024:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64> Cases.AsyncCase/'<L>d__5'::'<>t__builder'
    IL_0029:  call       instance class [mscorlib]System.Threading.Tasks.Task`1<!0> valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int64>::get_Task()
    IL_002e:  ret
  } // end of method AsyncCase::L

} // end of class Cases.AsyncCase


// =============================================================

// *********** 反汇编完成 ***********************
// 警告: 创建了 Win32 资源文件 tmp\asyncprobe\asynccase2.res
