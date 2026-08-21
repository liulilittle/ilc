//------------------------------------------------------------------------------
// main.cpp -- C API smoke test (M4 Task 7, extended).
//
// Links against the transpiled library (static form) and exercises the
// extern "C" API:
//   1. il2cpp_runtime_init initializes the runtime (idempotent -- the
//      repeated-init test calls it a second time).
//   2. il2cpp_gc_collect runs a collection without crashing.
//   3. il2cpp_type_from_name resolves a translated type.
//   4. il2cpp_method_invoke_i4 calls a transpiled static method.
//   5. Exception boundary: a throwing method returns 0 (never crosses the
//      extern "C" boundary) and records the last-exception message via
//      il2cpp_get_last_exception.
//   6. GC stability: a collection after many invokes leaves later invokes
//      intact (managed strings allocated by earlier calls are garbage).
//
// Build: the transpiled library is il2cpp_gen (static); this program links
// it and the runtime. See the test harness (run-capi.ps1 / CI).
//------------------------------------------------------------------------------

#include "il2cpp_api.h"

#include <cstdio>
#include <cstring>

int main()
{
    int failures = 0;
    auto Expect = [&](bool ok, const char* what) {
        if (!ok) { std::printf("FAIL: %s\n", what); ++failures; }
    };

    il2cpp_runtime_init();
    Expect(true, "il2cpp_runtime_init completes");

    il2cpp_gc_collect();
    Expect(true, "il2cpp_gc_collect completes");

    void* t = il2cpp_type_from_name("Program");
    Expect(t != nullptr, "il2cpp_type_from_name resolves a translated type");

    // Invoke Program.Add4(20, 22, 0, 0) == 42 (the transpiled static method,
    // exported as "Add4" by the transpiler's exact-signature filter).
    int32_t r = il2cpp_method_invoke_i4(t, "Add4", 20, 22, 0, 0);
    std::printf("invoke Add4 -> %d\n", r);
    Expect(r == 42, "il2cpp_method_invoke_i4 calls the transpiled method");

    // Negative: an unknown method name returns -1 (contract), no exception.
    int32_t missing = il2cpp_method_invoke_i4(t, "NoSuchMethod", 0, 0, 0, 0);
    Expect(missing == -1, "unknown method returns -1 without throwing");

    // Exception boundary 1: an explicit managed throw must not cross the
    // extern "C" boundary -- the invoke returns 0 and the last-exception
    // message records the thrown exception. (The M3 transpiler carries
    // every explicit throw as the fixed message "exception thrown"; the
    // fixture's literal message is bound in a later milestone -- see
    // CodeGenerator.cs throw_exception emission.)
    int32_t threw = il2cpp_method_invoke_i4(t, "Throw4", 1, 2, 3, 4);
    const char* last = il2cpp_get_last_exception();
    std::printf("invoke Throw4 -> %d, last_exception=\"%s\"\n", threw, last);
    Expect(threw == 0, "throwing method returns 0 (exception contract)");
    Expect(last != nullptr && std::strstr(last, "exception thrown") != nullptr,
           "last exception message records the thrown exception");

    // Exception boundary 2: integer division by zero throws
    // DivideByZeroException through the same boundary (checked division).
    int32_t div0 = il2cpp_method_invoke_i4(t, "Div4", 10, 0, 0, 0);
    last = il2cpp_get_last_exception();
    std::printf("invoke Div4(10,0,0,0) -> %d, last_exception=\"%s\"\n", div0, last);
    Expect(div0 == 0, "divide by zero returns 0 (exception contract)");
    Expect(last != nullptr && std::strstr(last, "divide by zero") != nullptr,
           "divide-by-zero message recorded");

    // Normal division still computes the exact quotient.
    int32_t div = il2cpp_method_invoke_i4(t, "Div4", 10, 2, 0, 0);
    std::printf("invoke Div4(10,2,0,0) -> %d\n", div);
    Expect(div == 5, "normal division returns the quotient");

    // GC stability: after several invokes (which allocated managed strings
    // that became garbage), a collection must not disturb later invokes.
    il2cpp_gc_collect();
    int32_t after = il2cpp_method_invoke_i4(t, "Add4", 20, 22, 0, 0);
    std::printf("invoke Add4 after gc_collect -> %d\n", after);
    Expect(after == 42, "invoke after gc_collect is stable");

    // Idempotent init: a second il2cpp_runtime_init must not crash and the
    // runtime must remain fully usable (export re-registration and type
    // registration are safe to repeat).
    il2cpp_runtime_init();
    Expect(true, "repeated il2cpp_runtime_init does not crash");
    int32_t again = il2cpp_method_invoke_i4(t, "Add4", 20, 22, 0, 0);
    Expect(again == 42, "invoke still works after repeated init");

    std::printf("capi smoke: %s\n", failures == 0 ? "all passed" : "FAILURES");
    return failures == 0 ? 0 : 1;
}
