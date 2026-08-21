//------------------------------------------------------------------------------
// task.cpp -- System.Threading.Tasks minimal synchronous surface (batch
// 155d + 156c): completed Task<T> (Task.FromResult) + the async state-machine
// surface. The async builder rides as a Task: Create() makes an incomplete
// task, SetResult completes it with the boxed value, get_Task() returns it.
// The transpiler lowers Start<T>(ref sm) to a DIRECT call of the generated
// MoveNext (synchronous completion -- every awaited task is completed, so
// the state machine runs to completion inside Start); the runtime never
// invokes generated code. AwaitUnsafeOnCompleted / OnCompleted /
// UnsafeOnCompleted are unreachable at runtime and throw loudly.
//------------------------------------------------------------------------------
#include "il2cpp_task.h"
#include "il2cpp_boxing_ext.h" // icall_box_double / icall_unbox_double (batch 163)
#include "il2cpp_boxing_ext2.h" // icall_box_long / icall_unbox_long (batch 160)
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_pinvoke.h"
#include "il2cpp_runtime.h"

using il2cpp::Il2CppObject;

namespace {

il2cpp::Il2CppClass System_Threading_Tasks_Task_1_class = {
    "System.Threading.Tasks.Task`1",        // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppTask)), // instanceSize
    // batch 156d: bit 1 = result, bit 3 = exception (the faulted-task
    // type-name string).
    0b01010u,                               // refFieldBitmap
    2,                                      // refFieldCount
    nullptr, nullptr, 0, nullptr, 0,
};

bool g_taskRegistered = false;

void EnsureTaskRegistered()
{
    if (g_taskRegistered) return;
    il2cpp_class_register(&System_Threading_Tasks_Task_1_class);
    g_taskRegistered = true;
}

} // namespace

namespace il2cpp::runtime {

// Task.FromResult<T>(T): a completed task carrying the boxed value.
Il2CppObject* icall_task_from_result(int32_t value)
{
    EnsureTaskRegistered();
    // The fresh task is the only allocation in this icall; the boxed value
    // is a frame-rooted parameter, so no collection can invalidate it
    // between the allocation and the result write.
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    task->result = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_box_int32(nullptr, value));
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result),
                         reinterpret_cast<Il2CppObject*>(task->result));
    task->isCompleted = 1;
    return reinterpret_cast<Il2CppObject*>(task);
}

// batch 160: Task<long>.FromResult<int64> -- the int64 carrier (Int64 box,
// full 64-bit payload). The int32 variant above stays for Task<int>.
Il2CppObject* icall_task_from_result_l(int64_t value)
{
    EnsureTaskRegistered();
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    task->result = icall_box_long(value);
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), task->result);
    task->isCompleted = 1;
    return reinterpret_cast<Il2CppObject*>(task);
}

// batch 163: Task<double>.FromResult<double> -- boxed double carrier.
Il2CppObject* icall_task_from_result_d(double value)
{
    EnsureTaskRegistered();
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    task->result = icall_box_double(value);
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), task->result);
    task->isCompleted = 1;
    return reinterpret_cast<Il2CppObject*>(task);
}

// batch 163: Task<string>.FromResult<string> -- the string object directly
// (strings ARE managed objects; no box).
Il2CppObject* icall_task_from_result_s(Il2CppObject* value)
{
    EnsureTaskRegistered();
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    task->result = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), value);
    task->isCompleted = 1;
    return reinterpret_cast<Il2CppObject*>(task);
}

// batch 165: Task<float>.FromResult<float> -- Single box (the M4 ABI carries
// float as double; icall_box_float stores the double at the aligned slot).
Il2CppObject* icall_task_from_result_f(double value)
{
    EnsureTaskRegistered();
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    auto* boxed = icall_box_float(value);
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
    return reinterpret_cast<Il2CppObject*>(task);
}

// batch 165: Task<ulong>.FromResult<ulong> -- UInt64 box (the int64_t param
// is the bit-preserving slot read; icall_box_ulong takes uint64_t).
Il2CppObject* icall_task_from_result_u(int64_t value)
{
    EnsureTaskRegistered();
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    auto* boxed = icall_box_ulong(static_cast<uint64_t>(value));
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
    return reinterpret_cast<Il2CppObject*>(task);
}

// batch 166: Task.Delay(int). The synchronous-completion model cannot
// represent a REAL delay (every awaited task completes synchronously inside
// Start -- there is no suspension, so a pending timer could never wake the
// state machine). net48 semantics: Task.Delay(0) returns an ALREADY-
// COMPLETED task (IsCompleted == True -- the zero-dueTime fast path); any
// ms > 0 throws NotSupportedException LOUDLY at runtime (the value is only
// known there; a transpile-time gate cannot see non-constant arguments).
// This keeps Task.Delay(0) -- the common test/synchronization idiom --
// byte-exact against net48 while every real delay stays a loud error.
Il2CppObject* icall_task_delay(int32_t milliseconds)
{
    if (milliseconds == 0)
    {
        EnsureTaskRegistered();
        auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
            il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
        task->result = nullptr;
        task->isCompleted = 1;
        return reinterpret_cast<Il2CppObject*>(task);
    }
    il2cpp::il2cpp_throw_new("System.NotSupportedException,System.Exception",
        "Task.Delay(n>0) is not representable in the synchronous-completion model "
        "(no suspension: every awaited task completes inside Start).");
    return nullptr;
}

// Task<T>.Result -- the boxed result (completed-task contract). batch 156d:
// a FAULTED task throws. batch 162: Task.Result wraps in AggregateException
// (net48 semantics -- the zh-CN message "\xE5\x8F\x91..." pinned below);
// the AWAITER path (icall_task_get_result_original) rethrows the ORIGINAL
// stored type (net48 GetAwaiter().GetResult() unwraps).
namespace {
void ThrowFaultedAggregate()
{
    il2cpp::il2cpp_throw_new("System.AggregateException,System.Exception",
        "\xE5\x8F\x91\xE7\x94\x9F\xE4\xB8\x80\xE4\xB8\xAA\xE6\x88\x96\xE5\xA4\x9A\xE4\xB8\xAA\xE9\x94\x99\xE8\xAF\xAF\xE3\x80\x82");
}
void ThrowFaultedOriginal(il2cpp::Il2CppTask* task)
{
    auto* name = task->exception;
    il2cpp::il2cpp_throw_new(il2cpp::il2cpp_pinvoke_string_to_utf8(name),
        "The task faulted (async state machine exception).");
}
} // namespace
int32_t icall_task_get_result(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedAggregate();
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return il2cpp::il2cpp_unbox_int32(task->result);
}

// TaskAwaiter<T>.GetResult() -- batch 162: rethrows the ORIGINAL stored
// exception type (net48 GetAwaiter().GetResult() unwraps the AggregateException
// that Task.Result wraps). The stored value is the M-model catch variable:
// the exception's SHORT type name as a managed string.
int32_t icall_task_get_result_original(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedOriginal(task);
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return il2cpp::il2cpp_unbox_int32(task->result);
}

// Task<long>.Result -- the int64 carrier (batch 160). The stored box is the
// Int64 box (icall_box_long) with the full 8-byte payload. batch 162:
// faulted -> AggregateException wrap (net48 Task.Result semantics).
int64_t icall_task_get_result_l(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedAggregate();
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return icall_unbox_long(task->result);
}

// TaskAwaiter<long>.GetResult() -- batch 162: rethrows the ORIGINAL stored
// exception type (net48 GetAwaiter().GetResult() unwraps).
int64_t icall_task_get_result_original_l(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedOriginal(task);
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return icall_unbox_long(task->result);
}

// batch 163: Task<double>.Result -- boxed double payload.
double icall_task_get_result_d(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedAggregate();
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return icall_unbox_double(task->result);
}

// TaskAwaiter<double>.GetResult() -- original-rethrow variant.
double icall_task_get_result_original_d(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedOriginal(task);
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return icall_unbox_double(task->result);
}

// batch 163: Task<string>.Result -- the result slot IS the string object.
Il2CppObject* icall_task_get_result_s(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedAggregate();
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return task->result;
}

// TaskAwaiter<string>.GetResult() -- original-rethrow variant.
Il2CppObject* icall_task_get_result_original_s(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedOriginal(task);
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return task->result;
}

// batch 165: Task<float>.Result -- the Single box payload read back as the
// double slot (unbox_float returns the stored double; float values ride
// double slots in the M4 ABI).
double icall_task_get_result_f(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedAggregate();
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return icall_unbox_float(task->result);
}

// TaskAwaiter<float>.GetResult() -- original-rethrow variant.
double icall_task_get_result_original_f(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedOriginal(task);
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return icall_unbox_float(task->result);
}

// batch 165: Task<ulong>.Result -- the UInt64 box payload read back as the
// bit-preserving int64_t slot (the model's ulong slot convention; the "q"
// cast at the consumer reinterprets unsigned).
int64_t icall_task_get_result_u(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedAggregate();
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return static_cast<int64_t>(icall_unbox_ulong(task->result));
}

// TaskAwaiter<ulong>.GetResult() -- original-rethrow variant.
int64_t icall_task_get_result_original_u(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->exception != nullptr)
    {
        ThrowFaultedOriginal(task);
    }
    if (task->isCompleted != 1)
    {
        il2cpp::il2cpp_throw_invalid_operation_exception("The task is not completed.");
    }
    return static_cast<int64_t>(icall_unbox_ulong(task->result));
}

// Task<T>.IsCompleted.
int32_t icall_task_get_is_completed(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    return reinterpret_cast<il2cpp::Il2CppTask*>(self)->isCompleted;
}

// Task<T>.GetAwaiter() -- batch 156c: the awaiter IS the task itself (the
// minimal synchronous awaiter contract). The state machine stores this ref
// in its TaskAwaiter slot and get_IsCompleted/GetResult deref it.
Il2CppObject* icall_task_get_awaiter(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    return self;
}

//------------------------------------------------------------------------------
// batch 156c: async state-machine builder surface (builder-as-task model).
//------------------------------------------------------------------------------

// AsyncTaskMethodBuilder<T>.Create(): a fresh INCOMPLETE task -- the
// builder object the state machine's <>t__builder field holds. The result
// is boxed at SetResult and the task completes then.
Il2CppObject* icall_async_builder_create()
{
    EnsureTaskRegistered();
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(
        il2cpp_object_new(&System_Threading_Tasks_Task_1_class));
    task->result = nullptr;
    task->isCompleted = 0;
    return reinterpret_cast<Il2CppObject*>(task);
}

// AsyncTaskMethodBuilder<T>.SetResult(T): complete the builder task with
// the boxed value. The builder is a frame-rooted parameter (the state
// machine's <>t__builder slot is a registered GC root), so the box
// allocation cannot invalidate it.
void icall_async_builder_set_result(Il2CppObject* self, int32_t value)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return; // idempotent: already completed
    auto* boxed = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_box_int32(nullptr, value));
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<T>.SetResult(T) for Task<long> (batch 160): the
// int64 carrier (Int64 box, full 8-byte payload).
void icall_async_builder_set_result_l(Il2CppObject* self, int64_t value)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return;
    auto* boxed = icall_box_long(value);
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<double>.SetResult (batch 163): boxed double carrier.
void icall_async_builder_set_result_d(Il2CppObject* self, double value)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return;
    auto* boxed = icall_box_double(value);
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<string>.SetResult (batch 163): the string object
// directly (no box).
void icall_async_builder_set_result_s(Il2CppObject* self, Il2CppObject* value)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return;
    task->result = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), value);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<float>.SetResult (batch 165): Single box carrier.
void icall_async_builder_set_result_f(Il2CppObject* self, double value)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return;
    auto* boxed = icall_box_float(value);
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<ulong>.SetResult (batch 165): UInt64 box carrier.
void icall_async_builder_set_result_u(Il2CppObject* self, int64_t value)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return;
    auto* boxed = icall_box_ulong(static_cast<uint64_t>(value));
    task->result = boxed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->result), boxed);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<T>.SetException(Exception): batch 156d -- the
// faulted-task model. The M-model catch variable is the exception's SHORT
// type name (an interned managed string); store it on the task and mark it
// completed (a faulted task IS completed). get_Result rethrows it.
void icall_async_builder_set_exception(Il2CppObject* self, Il2CppObject* ex)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    auto* task = reinterpret_cast<il2cpp::Il2CppTask*>(self);
    if (task->isCompleted == 1) return;
    if (ex == nullptr)
    {
        il2cpp::il2cpp_throw_argument_exception("SetException requires a non-null exception.");
    }
    task->exception = ex;
    il2cpp_write_barrier(reinterpret_cast<void**>(&task->exception), ex);
    task->isCompleted = 1;
}

// AsyncTaskMethodBuilder<T>.SetStateMachine: no-op in the synchronous model.
void icall_async_builder_set_state_machine(Il2CppObject* self)
{
    (void)self;
}

// AsyncTaskMethodBuilder<T>.get_Task(): the builder-as-task object.
Il2CppObject* icall_async_builder_get_task(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    return self;
}

// AwaitUnsafeOnCompleted / TaskAwaiter.OnCompleted / UnsafeOnCompleted:
// UNREACHABLE in the synchronous-completion model -- every awaited task is
// completed, so the state machine's IsCompleted branch skips suspension.
// A real suspension (incomplete task) is unsupported in the minimal
// surface: loud NotSupportedException.
void icall_async_builder_await_unsupported(Il2CppObject* self)
{
    (void)self;
    il2cpp::il2cpp_throw_not_supported_exception(
        "async continuation (AwaitUnsafeOnCompleted) is unreachable in the minimal "
        "synchronous Task surface: every awaited task must be completed (Task.FromResult).");
}

} // namespace il2cpp::runtime
