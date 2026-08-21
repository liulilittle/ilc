#ifndef IL2CPP_TASK_H
#define IL2CPP_TASK_H

#include "il2cpp_object.h"

//------------------------------------------------------------------------------
// il2cpp_task.h -- System.Threading.Tasks minimal synchronous surface
// (batch 155d): Task<T> completed tasks. The managed layout:
//   slot 0: object header
//   slot 1: the boxed result (Il2CppObject*)
//   slot 2 low half: isCompleted (1 = Task.FromResult completed task)
//------------------------------------------------------------------------------
namespace il2cpp {

// slot 0: object header
// slot 1: the boxed result (Il2CppObject*)
// slot 2 low half: isCompleted (1 = Task.FromResult completed task)
// slot 3: faulted exception TYPE-NAME string (Il2CppObject* -- the
//         M-model catch variable, batch 156d; null for success)
struct Il2CppTask
{
    Il2CppObject  _obj;
    Il2CppObject* result;
    int32_t       isCompleted;
    Il2CppObject* exception;
};

} // namespace il2cpp



namespace il2cpp::runtime {

// Task.FromResult<T>(T) -- a completed task carrying the boxed value.
Il2CppObject* icall_task_from_result(int32_t value);
// batch 166: Task.Delay(int) -- the synchronous-completion model cannot
// represent a REAL delay (no suspension); Delay(0) is a completed task
// (net48: Task.Delay(0).IsCompleted == True), any ms > 0 throws
// NotSupportedException at runtime (the value is only known there).
Il2CppObject* icall_task_delay(int32_t milliseconds);
// batch 160: Task<long>.FromResult<int64> -- int64 carrier (Int64 box).
Il2CppObject* icall_task_from_result_l(int64_t value);
// batch 163: Task<double>.FromResult<double> -- boxed double carrier.
Il2CppObject* icall_task_from_result_d(double value);
// batch 163: Task<string>.FromResult<string> -- the string object directly.
Il2CppObject* icall_task_from_result_s(Il2CppObject* value);
// batch 165: Task<float>.FromResult<float> -- Single box (the M4 ABI carries
// float as double; the box stores the double at the aligned payload slot).
Il2CppObject* icall_task_from_result_f(double value);
// batch 165: Task<ulong>.FromResult<ulong> -- UInt64 box; the int64_t param
// is the bit-preserving slot read (the same int64 slot convention as ulong
// values everywhere in the model).
Il2CppObject* icall_task_from_result_u(int64_t value);
// Task<T>.Result / .IsCompleted.
int32_t icall_task_get_result(Il2CppObject* self);
// batch 162: TaskAwaiter<T>.GetResult() -- rethrows the ORIGINAL stored
// exception type (net48 GetAwaiter().GetResult() unwraps the AggregateException
// that Task.Result wraps).
int32_t icall_task_get_result_original(Il2CppObject* self);
// batch 160: Task<long>.Result -- full int64 payload.
int64_t icall_task_get_result_l(Il2CppObject* self);
// batch 162: TaskAwaiter<long>.GetResult() -- original-rethrow variant.
int64_t icall_task_get_result_original_l(Il2CppObject* self);
// batch 163: Task<double>.Result -- boxed double payload (double carrier).
double icall_task_get_result_d(Il2CppObject* self);
double icall_task_get_result_original_d(Il2CppObject* self);
// batch 163: Task<string>.Result -- the result slot IS the string object
// (no box). Returns the string ref. batch 165: Task<object> rides the SAME
// ref carrier (the object IS the result -- no box).
Il2CppObject* icall_task_get_result_s(Il2CppObject* self);
Il2CppObject* icall_task_get_result_original_s(Il2CppObject* self);
// batch 165: Task<float>.Result -- the Single box payload read back as the
// double slot (unbox_float returns the stored double).
double icall_task_get_result_f(Il2CppObject* self);
double icall_task_get_result_original_f(Il2CppObject* self);
// batch 165: Task<ulong>.Result -- the UInt64 box payload read back as the
// bit-preserving int64_t slot (the model's ulong slot convention).
int64_t icall_task_get_result_u(Il2CppObject* self);
int64_t icall_task_get_result_original_u(Il2CppObject* self);
int32_t icall_task_get_is_completed(Il2CppObject* self);
// GetAwaiter() -- batch 156c: returns the task ITSELF as the awaiter (the
// minimal synchronous awaiter; the state machine's TaskAwaiter local holds
// this task ref and get_IsCompleted/GetResult deref it).
Il2CppObject* icall_task_get_awaiter(Il2CppObject* self);

//------------------------------------------------------------------------------
// batch 156c (asynccase): the async state-machine surface. The
// AsyncTaskMethodBuilder`1<T> value rides as a runtime object that IS a
// Task: Create() makes an incomplete task, SetResult completes it with the
// boxed value, get_Task() returns it. The transpiler lowers Start<T>(ref sm)
// to a DIRECT call of the generated MoveNext (synchronous completion), so
// the runtime never invokes generated code.
//------------------------------------------------------------------------------
// AsyncTaskMethodBuilder<T>.Create() -- a fresh incomplete task (the
// builder-as-task model; result boxed at SetResult).
Il2CppObject* icall_async_builder_create();
// AsyncTaskMethodBuilder<T>.SetResult(T) -- complete the builder task.
void icall_async_builder_set_result(Il2CppObject* self, int32_t value);
// batch 160: AsyncTaskMethodBuilder<long>.SetResult -- int64 carrier.
void icall_async_builder_set_result_l(Il2CppObject* self, int64_t value);
// batch 163: AsyncTaskMethodBuilder<double>.SetResult -- double carrier.
void icall_async_builder_set_result_d(Il2CppObject* self, double value);
// batch 163: AsyncTaskMethodBuilder<string>.SetResult -- string carrier.
void icall_async_builder_set_result_s(Il2CppObject* self, Il2CppObject* value);
// batch 165: AsyncTaskMethodBuilder<float>.SetResult -- Single box carrier.
void icall_async_builder_set_result_f(Il2CppObject* self, double value);
// batch 165: AsyncTaskMethodBuilder<ulong>.SetResult -- UInt64 box carrier.
void icall_async_builder_set_result_u(Il2CppObject* self, int64_t value);
// AsyncTaskMethodBuilder<T>.SetException(Exception) -- store the exception
// (minimal: the Result rethrow path is batch 156d).
void icall_async_builder_set_exception(Il2CppObject* self, Il2CppObject* ex);
// AsyncTaskMethodBuilder<T>.SetStateMachine -- no-op (synchronous model).
void icall_async_builder_set_state_machine(Il2CppObject* self);
// AsyncTaskMethodBuilder<T>.get_Task() -- the builder-as-task object.
Il2CppObject* icall_async_builder_get_task(Il2CppObject* self);
// AwaitUnsafeOnCompleted / TaskAwaiter.OnCompleted / UnsafeOnCompleted --
// UNREACHABLE in the synchronous-completion model (every awaited task is
// completed). Loud NotSupportedException if a real suspension is reached.
void icall_async_builder_await_unsupported(Il2CppObject* self);

} // namespace il2cpp::runtime
#endif // IL2CPP_TASK_H
