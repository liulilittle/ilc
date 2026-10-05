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
            // batch 165: Task<float> (0x0C -- the M4 ABI carries float as
            // double; Single box) and Task<ulong> (0x0B -- UInt64 box; the
            // bit-preserving int64_t slot convention). Task<object> (0x1C)
            // rides the SAME ref carrier as string (the object IS the result
            // -- no box; the "s" icalls take/return Il2CppObject*).
            var isFloat = instName?.Contains("_float", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x0C;
            var isUlong = instName?.Contains("_ulong", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x0B;
            var isObject = instName?.Contains("_object", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x1C;
            // batch 165b: reference-typed instantiations -- CLASS (0x12,
            // Task<AnyClass>) and SZARRAY (0x1D/0x1F, Task<int[]>) are
            // managed references and ride the SAME ref carrier as object.
            // The GenericInstantiationName arg table emits "class" / "array".
            var isClass = instName?.Contains("_class", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x12
                // A1 (asynccase Task<Point>): a dotted user-class
                // instantiation ("Task`1_Cases.AsyncCase+Point") rides the
                // SHARED ref carrier (the DottedRefArg kind rule, Queue
                // dotted-tag precedent). The tracked receiver tag carries
                // the same encoding when the parent is a bare TypeRef, so
                // the instName ?? receiverType tag decides (the List
                // precedent). BCL struct full names stay LOUD.
                || DottedRefArg(instName ?? receiverType) != null;
            var isArray = instName?.Contains("_array", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && (genArgs[0] == 0x1D || genArgs[0] == 0x1F);
            // batch 165b: VALUETYPE instantiations (Task<decimal> 0x11,
            // Task<DateTime> 0x11, Task<Guid> -- the 16-byte struct carriers
            // are NOT built): LOUD reject. Without this gate the int32
            // fallback silently truncates the struct value (Task<decimal>
            // compiled to icall_task_get_result(int32) and failed only later
            // at the decimal-compare site -- a confusing downstream error).
            var isValuetype = instName?.Contains("_valuetype", StringComparison.Ordinal) == true
                || genArgs is { Length: 1 } && genArgs[0] == 0x11
                // A1 guard (Task<DateTime> et al.): a dotted BCL struct
                // suffix must hit this LOUD gate, never the int32 fallback
                // below (which would silently truncate the struct value --
                // the hazard this gate exists for).
                || (DottedSuffix(instName ?? receiverType) is string ds && IsKnownValueTypeName(ds));
            // batch 165: the ref-return carriers (string, object, class and
            // array all ride the "s" icalls -- the result slot IS the object).
            var isRef = isString || isObject || isClass || isArray;
            // batch 163/165: per-carrier icall suffix + resultKind/cast
            // selector. String AND object ride as ref returns (ReturnsRef
            // handled by the caller via isRef); float returns the double
            // slot; "l"/"d" kinds type the consumer temp; "" = int32.
            (string Suffix, string Kind) Carrier()
            {
                // batch 165b: VALUETYPE result carriers (Task<decimal> /
                // Task<DateTime> / Task<Guid> -- the 16-byte struct boxes
                // are NOT built): LOUD reject here (Carrier() is only
                // reached by the RESULT-bearing ops -- SetResult/GetResult/
                // get_Result/FromResult -- never by Start<TStateMachine>,
                // whose TStateMachine genArg is itself a VALUETYPE struct).
                if (isValuetype)
                    throw new NotSupportedException(
                        $"Task<T> carrier for VALUETYPE T (instName '{instName}') is not built in M3 -- " +
                        "the 16-byte struct carriers (decimal/DateTime/Guid) are out of scope; only the " +
                        "primitive/ref carriers (int/long/double/float/string/object/ulong/bool/char/class/array) exist");
                return isRef ? ("_s", "")
                    : isFloat ? ("_f", "d")
                    : isUlong ? ("_u", "l")
                    : isDouble ? ("_d", "d")
                    : isInt64 ? ("_l", "l")
                    : ("", "");
            }
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
                            // Hash/prefix use the '+' canonical full name so the
                            // symbol matches TypeEmitter.MethodSymbolName, which
                            // hashes ReflectionFullName (Sanitize maps '+'/'.
                            // to '_' so the prefix still equals TypeCppName).
                            var moveNext = $"{Sanitize(smName)}_MoveNext_{SigHash.Hash(smName + ".MoveNext()")}";
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
                            // batch 165: float rides the double slot (no
                            // cast -- the value is already double); ulong
                            // rides the bit-preserving int64 slot ("l");
                            // string/object ride the ref slot (no cast).
                            var valueCast = isRef ? "" : isDouble || isFloat ? "d" : isInt64 || isUlong ? "l" : "";
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

            // batch diffexc001 (2026-10-05): the NON-generic
            // AsyncTaskMethodBuilder (behind `async Task` methods). Its
            // value-type locals lower to the opaque Il2CppObject* builder
            // carrier (CodeGenerator v:... branch), so the receiver is an
            // ADDRESS of that object slot -- deref:r reads the builder
            // object, exactly like the generic `1<T> fields (batch 156c).
            if (owner == "System.Runtime.CompilerServices.AsyncTaskMethodBuilder")
            {
                switch (name)
                {
                    // static AsyncTaskMethodBuilder Create(): a fresh runtime
                    // builder object (the task materializes via get_Task).
                    case "Create":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_async_builder_create", 0, true, true, false,
                                returnTypeName: "System.Runtime.CompilerServices.AsyncTaskMethodBuilder");
                        return null;
                    // instance bool get_IsCompleted(): unused by e2e probes.
                    case "get_IsCompleted":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_task_get_is_completed", 0, true, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "" });
                        return null;
                    // instance Task get_Task(): materialize the task from the
                    // builder object.
                    case "get_Task":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_async_builder_get_task", 0, true, true, true,
                                new[] { receiverIsAddress ? "deref:r" : "" },
                                returnTypeName: "System.Threading.Tasks.Task");
                        return null;
                    // instance void SetStateMachine(IAsyncStateMachine):
                    // no-op in the synchronous model.
                    case "SetStateMachine":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_async_builder_set_state_machine", 1, false, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "", "drop" });
                        return null;
                    // instance void SetException(Exception): store the
                    // exception on the builder (surface: minimal).
                    case "SetException":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_async_builder_set_exception", 1, false, false, true,
                                new[] { receiverIsAddress ? "deref:r" : "", "" });
                        return null;
                    // instance void AwaitUnsafeOnCompleted<TAwaiter,
                    // TStateMachine>(ref TAwaiter, ref TStateMachine):
                    // unreachable in the synchronous-completion model -- loud.
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
                            return I($"il2cpp::runtime::icall_task_get_result_original{suffix}", 0, true, isRef, true,
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
                case "Delay":
                    // batch 166: static Task Delay(int) -- a completed task
                    // for 0 (net48 fast path), loud NotSupportedException at
                    // runtime for ms > 0 (the synchronous-completion model
                    // cannot represent a real suspension).
                    if (count == 1)
                        return I("il2cpp::runtime::icall_task_delay", 1, true, true, false,
                            null, null, false, false, false, returnTypeName: owner);
                    return null;
                case "FromResult":
                    // static Task<T> FromResult<T>(T) -- a completed task
                    // carrying the boxed value. batch 160/163: the generic
                    // argument selects the carrier (int64/double/string);
                    // the "s"/"d"/"l" cast codes type the value argument.
                    if (count == 1)
                    {
                        var (suffix, _) = Carrier();
                        // batch 165: string/object ride the ref slot (no
                        // cast); float the double slot ("d"); ulong the
                        // int64 slot ("l").
                        var valueCast = isRef ? "" : isDouble || isFloat ? "d" : isInt64 || isUlong ? "l" : "";
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
                        return I($"il2cpp::runtime::icall_task_get_result{suffix}", 0, true, isRef, true,
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
                case "Run":
                    // exc001: static Task<T> Run<T>(Func<T> function) -- the
                    // synchronous-completion model runs the delegate NOW and
                    // wraps the value in a completed task (Run(f) ≡
                    // FromResult(f.Invoke())). Generic over carriers by the
                    // same table as FromResult (int-family -> plain, ref
                    // carriers -> _s); the value slot's "run:i"/"run:r" cast
                    // synchronously invokes the zero-arg delegate through the
                    // existing multicast ABIs (CodeGenerator.cs). The outer
                    // call shape (icall/ParamCount/ReturnTypeName) is
                    // IDENTICAL to FromResult, so Run<T> and FromResult<T>
                    // stay one mechanism -- no per-method emission.
                    // LOUD (null) faces, each a documented unsupported shape:
                    // non-Task owner (Task<T> declares no Run), != 1 arg
                    // (CancellationToken overloads), non-CLASS param (never a
                    // delegate -- GenericInst Func<T> is 0x15), non-generic
                    // shape (Run(Action) has no MethodSpec), non-Func
                    // delegate hint, T outside the allowlist (long/ulong/
                    // double/float have no zero-arg multicast ABI -- raw
                    // methodPtr would read a multicast container as code;
                    // VALUETYPE structs need unbuilt 16-byte carriers; OPEN
                    // VAR/MVAR, nested GenericInst (Task<T> unwrapping needs
                    // a monad join), POINTER/native-int are never exact).
                    if (owner == "System.Threading.Tasks.Task" && count == 1 && ps[0] is 0x12 or 0x15
                        && genArgs is { Length: 1 }
                        && (arg0Type == null || arg0Type.StartsWith("System.Func`", StringComparison.Ordinal))
                        && genArgs[0] is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09
                            or 0x0E or 0x1C or 0x12 or 0x1D or 0x1F)
                    {
                        var isRunRef = genArgs[0] is 0x0E or 0x1C or 0x12 or 0x1D or 0x1F;
                        var runSuffix = isRunRef ? "_s" : "";
                        var invokeCast = isRunRef ? "run:r" : "run:i";
                        return I($"il2cpp::runtime::icall_task_from_result{runSuffix}", 1, true, true, false,
                            new[] { invokeCast, "" }, returnTypeName: "System.Threading.Tasks.Task`1");
                    }
                    return null;
                default:
                    return null;
            }
        }
    }
}
