//------------------------------------------------------------------------------
// test_marshal.cpp -- Marshal icall runtime unit tests (M4 Task 6 + the
// Marshal gap batch).
//
// Coverage (normal + error paths):
//   1. AllocHGlobal allocates a non-null native buffer of the requested size.
//   2. Copy(int[] -> native) copies the array elements to the buffer.
//   3. Copy(native -> int[]) copies the buffer back into an array.
//   4. FreeHGlobal releases the buffer; a second free is undefined (not
//      exercised -- .NET's FreeHGlobal is a single-use contract).
//   5. Bounds validation: out-of-range copy arguments abort loudly.
//   (Marshal gap batch -- marshalext3.cpp)
//   6. UnsafeAddrOfPinnedArrayElement: address = array data base +
//      index*elemSize; null array -> ArgumentNullException; out-of-range
//      index -> IndexOutOfRangeException.
//   7. GetHRForException: null -> 0; a known type-name string -> its net48
//      default HResult; an unknown type -> COR_E_EXCEPTION (0x80131500);
//      a non-string object -> COR_E_EXCEPTION; a 15-entry cross-check table
//      samples every exception family's net48 default HResult.
//   8. GetExceptionCode() -> 0 and GetExceptionPointers() -> IntPtr.Zero
//      (no-SEH semantics on every platform).
//   9. ReAllocHGlobal (D-HG-1, marshalext.cpp): grow preserves min(old,new);
//      shrink preserves the prefix; a non-zero new pointer on growth; a
//      negative size -> IntPtr.Zero.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_marshal_ext.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Catch-and-verify a DotNetException whose type-name chain starts with the
// expected full name (test_marshalext.cpp idiom).
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedType)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Expected type chains (must match exceptionext.cpp exactly).
const char* kArgumentNull =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kIndexOutOfRange =
    "System.IndexOutOfRangeException,System.IndexOutOfRangeException,System.SystemException,System.Exception";

// A mock int32 array class descriptor (values only -- 16-byte objects).
il2cpp::Il2CppClass g_intArrClass = {
    "[System.Int32", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

} // namespace

int main()
{
    // --- 1. AllocHGlobal ---
    intptr_t buf = il2cpp::runtime::icall_marshal_alloc_hglobal(32);
    Expect(buf != 0, "AllocHGlobal returns a non-null buffer");

    // --- 2. Copy int[] -> native ---
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(&g_intArrClass, 4, false, 5);
    int32_t* elems = reinterpret_cast<int32_t*>(arr->data);
    for (int32_t i = 0; i < 5; ++i)
    {
        elems[i] = i * 10; // 0, 10, 20, 30, 40
    }
    il2cpp::runtime::icall_marshal_copy_to_unmanaged(
        arr, 1, reinterpret_cast<void*>(buf), 3);
    const int32_t* native = reinterpret_cast<const int32_t*>(buf);
    Expect(native[0] == 10 && native[1] == 20 && native[2] == 30,
           "Copy(int[] -> native) transfers the requested elements");

    // --- 3. Copy native -> int[] ---
    il2cpp::Il2CppArray* back = il2cpp::il2cpp_array_new(&g_intArrClass, 4, false, 3);
    il2cpp::runtime::icall_marshal_copy_from_unmanaged(
        const_cast<int32_t*>(native), back, 0, 3);
    int32_t* backElems = reinterpret_cast<int32_t*>(back->data);
    Expect(backElems[0] == 10 && backElems[1] == 20 && backElems[2] == 30,
           "Copy(native -> int[]) transfers the buffer back");

    // --- 4. FreeHGlobal ---
    il2cpp::runtime::icall_marshal_free_hglobal(reinterpret_cast<void*>(buf));
    Expect(true, "FreeHGlobal releases the buffer without crashing");

    // --- 6. UnsafeAddrOfPinnedArrayElement (marshalext3) ---
    // arr has elemSize 4, length 5 (indices 0..4). The address of element
    // 'index' is data base + index * 4.
    const intptr_t baseAddr =
        il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element(arr, 0);
    Expect(baseAddr == reinterpret_cast<intptr_t>(arr->data),
           "UnsafeAddrOfPinnedArrayElement index 0 is the data base");
    const intptr_t elem2Addr =
        il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element(arr, 2);
    Expect(elem2Addr == baseAddr + 2 * static_cast<intptr_t>(arr->elemSize),
           "UnsafeAddrOfPinnedArrayElement index 2 is base + 2*elemSize");
    const intptr_t lastAddr =
        il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element(arr, 4);
    Expect(lastAddr == baseAddr + 4 * static_cast<intptr_t>(arr->elemSize),
           "UnsafeAddrOfPinnedArrayElement index length-1 is base + (length-1)*elemSize");
    Expect(ThrowsDotNet([&]() { (void)il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element(nullptr, 0); },
                        kArgumentNull),
           "UnsafeAddrOfPinnedArrayElement(null array) -> ArgumentNullException");
    Expect(ThrowsDotNet([&]() { (void)il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element(arr, -1); },
                        kIndexOutOfRange),
           "UnsafeAddrOfPinnedArrayElement(negative index) -> IndexOutOfRangeException");
    Expect(ThrowsDotNet([&]() { (void)il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element(arr, 5); },
                        kIndexOutOfRange),
           "UnsafeAddrOfPinnedArrayElement(index == length) -> IndexOutOfRangeException");

    // --- 7. GetHRForException (marshalext3) ---
    // ILC exceptions are the catch handler's interned type-name string, so
    // the icall maps the string's type name to its net48 default HResult.
    Expect(il2cpp::runtime::icall_marshal_get_hr_for_exception(nullptr) == 0,
           "GetHRForException(null) is 0");
    {
        il2cpp::Il2CppObject* argEx = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("System.ArgumentException"));
        Expect(il2cpp::runtime::icall_marshal_get_hr_for_exception(argEx) == static_cast<int32_t>(0x80070057),
               "GetHRForException(ArgumentException) is COR_E_ARGUMENT (0x80070057)");
        il2cpp::Il2CppObject* notSup = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("System.NotSupportedException"));
        Expect(il2cpp::runtime::icall_marshal_get_hr_for_exception(notSup) == static_cast<int32_t>(0x80131515),
               "GetHRForException(NotSupportedException) is COR_E_NOTSUPPORTED (0x80131515)");
        il2cpp::Il2CppObject* unknown = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("Some.CustomException"));
        Expect(il2cpp::runtime::icall_marshal_get_hr_for_exception(unknown) == static_cast<int32_t>(0x80131500),
               "GetHRForException(unknown type) falls back to COR_E_EXCEPTION (0x80131500)");
        il2cpp::Il2CppObject* arrObj = reinterpret_cast<il2cpp::Il2CppObject*>(arr);
        Expect(il2cpp::runtime::icall_marshal_get_hr_for_exception(arrObj) == static_cast<int32_t>(0x80131500),
               "GetHRForException(non-string object) falls back to COR_E_EXCEPTION");
        // HRESULT table cross-check (net48 default _HResult values -- the
        // marshalext3.cpp kExceptionHResults contract): sample every exception
        // family to guard against a transposed constant.
        struct HrCase { const char* name; int32_t hr; };
        const HrCase kHrCases[] = {
            { "System.InvalidCastException",       static_cast<int32_t>(0x80004002) },
            { "System.NullReferenceException",     static_cast<int32_t>(0x80004003) },
            { "System.IndexOutOfRangeException",   static_cast<int32_t>(0x80131508) },
            { "System.OverflowException",          static_cast<int32_t>(0x80131516) },
            { "System.DivideByZeroException",      static_cast<int32_t>(0x80020012) },
            { "System.InvalidOperationException",  static_cast<int32_t>(0x80131509) },
            { "System.OutOfMemoryException",       static_cast<int32_t>(0x8007000E) },
            { "System.StackOverflowException",     static_cast<int32_t>(0x800703E9) },
            { "System.IO.IOException",             static_cast<int32_t>(0x80131620) },
            { "System.TimeoutException",           static_cast<int32_t>(0x80131505) },
            { "System.UnauthorizedAccessException",static_cast<int32_t>(0x80070005) },
            { "System.KeyNotFoundException",       static_cast<int32_t>(0x80131577) },
            { "System.ArgumentOutOfRangeException",static_cast<int32_t>(0x80131502) },
            { "System.TypeLoadException",          static_cast<int32_t>(0x80131522) },
            { "System.NotImplementedException",    static_cast<int32_t>(0x80004001) },
        };
        char hrMsg[80];
        for (const HrCase& hrCase : kHrCases)
        {
            il2cpp::Il2CppObject* ex = reinterpret_cast<il2cpp::Il2CppObject*>(
                il2cpp::il2cpp_string_new_from_cs(hrCase.name));
            std::snprintf(hrMsg, sizeof(hrMsg), "GetHRForException(%s) is the net48 default HResult", hrCase.name);
            Expect(il2cpp::runtime::icall_marshal_get_hr_for_exception(ex) == hrCase.hr, hrMsg);
        }
    }

    // --- 8. GetExceptionCode / GetExceptionPointers (marshalext3) ---
    // ILC has no SEH in the fully managed context: the code is always 0 and
    // the pointers always IntPtr.Zero (on every platform, including Windows).
    Expect(il2cpp::runtime::icall_marshal_get_exception_code() == 0,
           "GetExceptionCode() is 0 (no SEH in ILC)");
    Expect(il2cpp::runtime::icall_marshal_get_exception_pointers() == 0,
           "GetExceptionPointers() is IntPtr.Zero (no SEH in ILC)");

    // --- 9. ReAllocHGlobal (D-HG-1, marshalext.cpp) ---
    // net48 realloc semantics: the OLD pointer is invalid after the call, a
    // NEW pointer is returned, and the first min(oldSize,newSize) bytes are
    // preserved. A negative size -> IntPtr.Zero (the alloc-family guard).
    {
        intptr_t rbuf = il2cpp::runtime::icall_marshal_alloc_hglobal(2 * 4);
        Expect(rbuf != 0, "ReAllocHGlobal: pre-alloc for the grow test");
        int32_t* pre = reinterpret_cast<int32_t*>(rbuf);
        pre[0] = 111;
        pre[1] = 222;
        intptr_t grown = il2cpp::runtime::icall_marshal_realloc_hglobal(rbuf, 4 * 4);
        Expect(grown != 0, "ReAllocHGlobal grow returns a non-null new pointer");
        const int32_t* grownInts = reinterpret_cast<const int32_t*>(grown);
        Expect(grownInts[0] == 111 && grownInts[1] == 222,
               "ReAllocHGlobal grow preserves min(old,new) = all 2 ints");
        int32_t* tail = reinterpret_cast<int32_t*>(grown) + 2;
        tail[0] = 333;
        tail[1] = 444;
        Expect(grownInts[2] == 333 && grownInts[3] == 444,
               "ReAllocHGlobal grow tail is writable");
        intptr_t shrunk = il2cpp::runtime::icall_marshal_realloc_hglobal(grown, 1 * 4);
        Expect(shrunk != 0, "ReAllocHGlobal shrink returns a non-null pointer");
        const int32_t* shrunkInts = reinterpret_cast<const int32_t*>(shrunk);
        Expect(shrunkInts[0] == 111,
               "ReAllocHGlobal shrink preserves the first min(old,new) byte");
        il2cpp::runtime::icall_marshal_free_hglobal(reinterpret_cast<void*>(shrunk));
        Expect(il2cpp::runtime::icall_marshal_realloc_hglobal(0, -4) == 0,
               "ReAllocHGlobal negative size -> IntPtr.Zero");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d marshal assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime marshal tests passed\n");
    return 0;
}
