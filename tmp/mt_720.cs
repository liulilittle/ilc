//------------------------------------------------------------------------------
// MethodMap.Task -- System.Threading.Tasks minimal synchronous surface
// (batch 155d + 156c): completed Task<T> (Task.FromResult) + the async
// state-machine surface. The async state machine <F>d__1 is a nested struct
// whose builder/awaiter fields are single Il2CppObject* slots:
//
//   * AsyncTaskMethodBuilder`1<T>  = a runtime "builder" object carrying the
//     result (task.cpp il2cpp_async_builder_create/set_result/get_task).
//   * TaskAwaiter`1<T>             = the task itself (the minimal awaiter).
//
//   Start<TStateMachine>(ref T) does NOT go through the runtime: the
//   transpiler lowers it to a DIRECT call of the generated MoveNext (the
//   synchronous-completion model -- every awaited task is completed, so the
//   state machine runs to completion inside Start). AwaitUnsafeOnCompleted /
//   OnCompleted / UnsafeOnCompleted are never reached at runtime (all
//   awaited tasks are completed); they map to a LOUD NotSupportedException
//   icall (the "unreachable = loud" doctrine, net-domain.md).
//------------------------------------------------------------------------------
namespace Il2Cpp.Transpiler.Emit
{
    public static partial class MethodRefResolver
    {
        private static ResolvedMethod? TryMapTask(string owner, string name, byte[] ps, int count,
            byte[]? genArgs, string? receiverType, bool receiverIsAddress,
            string? arg0Type, string? arg1Type, string[]? genArgNames = null, byte ret = 0x08,
            string? instName = null)
        {
            // batch 160/163: the generic INSTANTIATION name (TypeSpec parent,
            // e.g. "System.Runtime.CompilerServices.TaskAwaiter`1_long" /
            // "..._double" / "..._string"). The suffix selects the result
            // carrier for member calls whose signature carries only !0 (0x13
            // VAR). genArgs covers the static FromResult<T> shape.
            var isInt64 = instName?.Contains("_long", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x0A;
            var isDouble = instName?.Contains("_double", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x0D;
            var isString = instName?.Contains("_string", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x0E;
            // batch 163: per-carrier icall suffix + resultKind/cast selector.
            // String rides as a ref return (ReturnsRef handled by the caller);
            // "l"/"d" kinds type the consumer temp; "" = int32.
            (string Suffix, string Kind) Carrier() => isString ? ("_s", "")
                : isDouble ? ("_d", "d")
                : isInt64 ? ("_l", "l")
                : ("", "");
            // batch 156c: the async builder surface.
            if (owner == "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1")
            {
                switch (name)
                {
                    // static AsyncTaskMethodBuilder<T> Create(): a fresh
                    // runtime builder object (holds the result; the task is
                    // materialized by get_Task).
                    case "Create":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_async_builder_create", 0, true, true, false,
                                returnTypeName: "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1");
                        return null;
                    // instance void Start<TStateMachine>(ref TStateMachine):
                    // lowered to a DIRECT call of the generated MoveNext with
                    // the state machine ADDRESS as `this`. The builder
                    // receiver is dropped. The generated symbol is
                    // {TypeCppName}_MoveNext_{SigHash} -- the same
                    // MethodSymbolName the nested type's method body emits
                    // (TypeEmitter.MethodSymbolName); genArgNames[0] carries
                    // the state machine full name.
                    case "Start":
                        if (genArgNames is { Length: 1 } && genArgNames[0] is { Length: > 0 } smName)
                        {
                            var shortName = smName.Contains('+')
                                ? smName[(smName.LastIndexOf('+') + 1)..]
                                : smName;
                            var moveNext = $"{Sanitize(shortName)}_MoveNext_{SigHash.Hash(shortName + ".MoveNext()")}";
                            return I(moveNext, 1, false, false, true,
                                new[] { "drop", "" });
                        }
                        return null;
                    // instance void SetResult(T): store the value into the
                    // builder object (the receiver is the builder FIELD
                    // address -- deref:r reads the builder object). batch
                    // 160/163: the generic INSTANTIATION selects the carrier
                    // with the matching value cast.
                    case "SetResult":
                        if (count == 1)
                        {
                            var (suffix, _) = Carrier();
                            var valueCast = isString ? "" : isDouble ? "d" : isInt64 ? "l" : "";
                            return I($"il2cpp::runtime::icall_async_builder_set_result{suffix}", 1, false, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "", valueCast });
                        }
                        return null;
                    // instance void SetException(Exception): store the
                    // exception on the builder (surface: minimal -- the
                    // Result path does not rethrow yet, batch 156d).
                    case "SetException":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_async_builder_set_exception", 1, false, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "", "" });
                        return null;
                    // instance void SetStateMachine(IAsyncStateMachine):
                    // no-op in the synchronous model.
                    case "SetStateMachine":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_async_builder_set_state_machine", 1, false, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "", "drop" });
                        return null;
                    // instance Task<T> get_Task(): materialize the completed
                    // task from the stored result.
                    case "get_Task":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_async_builder_get_task", 0, true, true, true,
                                new[] { receiverIsAddress ? "deref:r" : "" },
                                returnTypeName: "System.Threading.Tasks.Task`1");
                        return null;
                    // instance void AwaitUnsafeOnCompleted<TAwaiter,TStateMachine>
                    // (ref TAwaiter, ref TStateMachine): UNREACHABLE in the
                    // synchronous-completion model -- every awaited task is
                    // completed (get_IsCompleted true), so the state machine
                    // never suspends. Loud NotSupportedException if reached.
                    case "AwaitUnsafeOnCompleted":
                        return I("il2cpp::runtime::icall_async_builder_await_unsupported", 2, false, false, true,
                            new[] { receiverIsAddress ? "deref:r" : "", "drop", "drop" });
                    default:
                        return null;
                }
            }

            // batch 156c: the awaiter surface -- the awaiter is the task.
            if (owner == "System.Runtime.CompilerServices.TaskAwaiter`1")
            {
                switch (name)
                {
                    // instance bool get_IsCompleted(): the task's completion.
                    case "get_IsCompleted":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_task_get_is_completed", 0, true, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "" });
                        return null;
                    // instance T GetResult(): the task's result. batch
                    // 160/163: the generic INSTANTIATION selects the carrier
                    // (int64/double/string); resultKind types the consumer
                    // temp. batch 162: the AWAITER path rethrows the ORIGINAL
                    // exception (net48 GetAwaiter().GetResult() unwraps the
                    // AggregateException that Task.Result wraps).
                    case "GetResult":
                        if (count == 0)
                        {
                            var (suffix, kind) = Carrier();
                            return I($"il2cpp::runtime::icall_task_get_result_original{suffix}", 0, true, isString, true,
                                new[] { receiverIsAddress ? "deref:r" : "" },
                                resultKind: kind.Length > 0 ? kind : null);
                        }
                        return null;
                    // instance void OnCompleted(Action) / UnsafeOnCompleted
                    // (Action): UNREACHABLE (awaits never suspend); loud.
                    case "OnCompleted":
                    case "UnsafeOnCompleted":
                        return I("il2cpp::runtime::icall_async_builder_await_unsupported", 1, false, false, true,
                            new[] { receiverIsAddress ? "deref:r" : "", "drop" });
                    default:
                        return null;
                }
            }

            switch (name)
            {
                case "FromResult":
                    // static Task<T> FromResult<T>(T) -- a completed task
                    // carrying the boxed value. batch 160/163: the generic
                    // argument selects the carrier (int64/double/string);
                    // the "s"/"d"/"l" cast codes type the value argument.
                    if (count == 1)
                    {
                        var (suffix, _) = Carrier();
                        var valueCast = isString ? "" : isDouble ? "d" : isInt64 ? "l" : "";
                        return I($"il2cpp::runtime::icall_task_from_result{suffix}", 1, true, true, false,
                            new[] { valueCast, "" }, returnTypeName: owner);
                    }
                    return null;
                case "get_Result":
                    // Task<T>.Result -- the boxed result. batch 160/163: the
                    // generic INSTANTIATION selects the carrier; resultKind
                    // types the consumer temp (no truncation / mis-cast).
                    if (count == 0)
                    {
                        var (suffix, kind) = Carrier();
                        return I($"il2cpp::runtime::icall_task_get_result{suffix}", 0, true, isString, true,
                            resultKind: kind.Length > 0 ? kind : null);
                    }
                    return null;
                case "get_IsCompleted":
                    // Task<T>.IsCompleted.
                    if (count == 0)
                        return I("il2cpp::runtime::icall_task_get_is_completed", 0, true, false, true);
                    return null;
                case "GetAwaiter":
                    // Task<T>.GetAwaiter() -- batch 156c: returns the task
                    // ITSELF as the awaiter (Il2CppObject* ref -- the state
                    // machine's TaskAwaiter local/slot is a ref slot holding
                    // the task; get_IsCompleted/GetResult deref it).
                    if (count == 0)
                        return I("il2cpp::runtime::icall_task_get_awaiter", 0, true, true, true,
                            returnTypeName: "System.Runtime.CompilerServices.TaskAwaiter`1");
                    return null;
                default:
                    return null;
            }
        }
    }
}
