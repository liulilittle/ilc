
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
  .custom instance void [mscorlib]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = ( 01 00 2E 31 2E 30 2E 30 2B 65 61 33 66 64 32 31   // ...1.0.0+ea3fd21
                                                                                                             37 37 36 62 62 64 37 64 64 35 62 37 38 35 66 61   // 776bbd7dd5b785fa
                                                                                                             36 32 64 39 63 34 33 38 30 31 66 30 38 34 34 63   // 62d9c43801f0844c
                                                                                                             36 00 00 )                                        // 6..
  .custom instance void [mscorlib]System.Reflection.AssemblyProductAttribute::.ctor(string) = ( 01 00 09 61 73 79 6E 63 63 61 73 65 00 00 )       // ...asynccase..
  .custom instance void [mscorlib]System.Reflection.AssemblyTitleAttribute::.ctor(string) = ( 01 00 09 61 73 79 6E 63 63 61 73 65 00 00 )       // ...asynccase..
  .hash algorithm 0x00008004
  .ver 1:0:0:0
}
.module asynccase.exe
// MVID: {DF30C8C5-D059-4905-8397-FAB22DBAF331}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x06BC0000


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
      // 代码大小       246 (0xf6)
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
        IL_000c:  beq        IL_00a4

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
        IL_0046:  leave      IL_00f5

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
        IL_006e:  ldc.i4.4
        IL_006f:  add
        IL_0070:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
        IL_0075:  callvirt   instance valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<!0> class [mscorlib]System.Threading.Tasks.Task`1<int32>::GetAwaiter()
        IL_007a:  stloc.2
        IL_007b:  ldloca.s   V_2
        IL_007d:  call       instance bool valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::get_IsCompleted()
        IL_0082:  brtrue.s   IL_00c0

        IL_0084:  ldarg.0
        IL_0085:  ldc.i4.1
        IL_0086:  dup
        IL_0087:  stloc.0
        IL_0088:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_008d:  ldarg.0
        IL_008e:  ldloc.2
        IL_008f:  stfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_0094:  ldarg.0
        IL_0095:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
        IL_009a:  ldloca.s   V_2
        IL_009c:  ldarg.0
        IL_009d:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::AwaitUnsafeOnCompleted<valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>,valuetype Cases.AsyncCase/'<H>d__3'>(!!0&,
                                                                                                                                                                                                                                                                     !!1&)
        IL_00a2:  leave.s    IL_00f5

        IL_00a4:  ldarg.0
        IL_00a5:  ldfld      valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_00aa:  stloc.2
        IL_00ab:  ldarg.0
        IL_00ac:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32> Cases.AsyncCase/'<H>d__3'::'<>u__1'
        IL_00b1:  initobj    valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>
        IL_00b7:  ldarg.0
        IL_00b8:  ldc.i4.m1
        IL_00b9:  dup
        IL_00ba:  stloc.0
        IL_00bb:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_00c0:  ldloca.s   V_2
        IL_00c2:  call       instance !0 valuetype [mscorlib]System.Runtime.CompilerServices.TaskAwaiter`1<int32>::GetResult()
        IL_00c7:  stloc.1
        IL_00c8:  leave.s    IL_00e1

      }  // end .try
      catch [mscorlib]System.Exception 
      {
        IL_00ca:  stloc.3
        IL_00cb:  ldarg.0
        IL_00cc:  ldc.i4.s   -2
        IL_00ce:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
        IL_00d3:  ldarg.0
        IL_00d4:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
        IL_00d9:  ldloc.3
        IL_00da:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetException(class [mscorlib]System.Exception)
        IL_00df:  leave.s    IL_00f5

      }  // end handler
      IL_00e1:  ldarg.0
      IL_00e2:  ldc.i4.s   -2
      IL_00e4:  stfld      int32 Cases.AsyncCase/'<H>d__3'::'<>1__state'
      IL_00e9:  ldarg.0
      IL_00ea:  ldflda     valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32> Cases.AsyncCase/'<H>d__3'::'<>t__builder'
      IL_00ef:  ldloc.1
      IL_00f0:  call       instance void valuetype [mscorlib]System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1<int32>::SetResult(!0)
      IL_00f5:  ret
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

  .method public hidebysig static void  Main() cil managed
  {
    .entrypoint
    // 代码大小       61 (0x3d)
    .maxstack  8
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
    IL_003c:  ret
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
    // 代码大小       9 (0x9)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  ldc.i4.1
    IL_0002:  add
    IL_0003:  call       class [mscorlib]System.Threading.Tasks.Task`1<!!0> [mscorlib]System.Threading.Tasks.Task::FromResult<int32>(!!0)
    IL_0008:  ret
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

} // end of class Cases.AsyncCase


// =============================================================

// *********** 反汇编完成 ***********************
// 警告: 创建了 Win32 资源文件 tmp\asyncprobe\asynccase.res
