//------------------------------------------------------------------------------
// test_strjoin.cpp -- large multi-segment string operations that previously
// corrupted the managed heap (strjoin e2e, 0xC0000374).
//
// Root cause (fixed in gc.cpp): a single managed allocation LARGER than a
// Gen0 segment's 64KB payload but BELOW the 85000 LOH threshold was bumped
// into a fresh 64KB segment, running past the segment boundary (heap
// corruption). The 10000-element string[] (80032 bytes) and the StringBuilder
// char buffer at 32768 units (65568 bytes) both hit this window.
//
// Coverage:
//   1.  Small join (3 elements) -- baseline sanity.
//   2.  Big array: 10000-element string[] join == expected length/content.
//   3.  Big empty-sep join length.
//   4.  StringBuilder: 10000-iteration append_c/append_i round trip.
//   5.  Object[] join (boxed + string + null elements).
//   6.  Concat convenience (int/bool).
//------------------------------------------------------------------------------

#include "il2cpp_string_ext2.h"

#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_concat.h"
#include "il2cpp_format.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "il2cpp_stringbuilder.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

using namespace il2cpp;

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

Il2CppString* Cs(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

Il2CppObject* Obj(const char* ascii)
{
    return reinterpret_cast<Il2CppObject*>(Cs(ascii));
}

Il2CppString* AsString(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppString*>(obj);
}

Il2CppArray* RefArr(int n)
{
    return il2cpp_array_new(nullptr, sizeof(void*), true, n);
}

void SetElem(Il2CppArray* arr, int index, Il2CppObject* value)
{
    reinterpret_cast<Il2CppObject**>(arr->data)[index] = value;
}

// Payload equality: content matches 'text' (null-terminated) unit for unit
// and the length matches the literal's unit count.
bool WideEqual(const Il2CppString* s, const char16_t* text)
{
    int32_t len = 0;
    while (text[len] != u'\0')
    {
        ++len;
    }
    if (s == nullptr || s->length != len)
    {
        return false;
    }
    for (int32_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != text[i])
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    il2cpp::runtime::runtime_init();

    // Frame rooting contract (mirrors generated Main): reference locals that
    // survive allocations are rooted; the collector may move them.
    Il2CppObject* loc3 = nullptr;
    Il2CppObject* loc4 = nullptr;
    Il2CppObject* loc5 = nullptr;
    Il2CppObject* strE = Obj("e");
    Il2CppObject* strComma = Obj(",");
    void* refs[] = { (void*)&loc3, (void*)&loc4, (void*)&loc5, (void*)&strE, (void*)&strComma };
    Il2CppFrame __frame = { nullptr, nullptr, nullptr, 0 };
    __frame.methodName = "test_strjoin";
    __frame.refs = (void**)refs;
    __frame.refCount = 5;
    il2cpp::il2cpp_frame_push(&__frame);

    // 1. Small join baseline.
    {
        Il2CppArray* arr = RefArr(3);
        SetElem(arr, 0, Obj("a"));
        SetElem(arr, 1, Obj("b"));
        SetElem(arr, 2, Obj("c"));
        Il2CppString* r = AsString(il2cpp::runtime::icall_string_join(Obj(","), reinterpret_cast<Il2CppObject*>(arr)));
        Expect(WideEqual(r, u"a,b,c") && r->length == 5,
               "small join(a,b,c) == \"a,b,c\" (len 5)");
    }

    // 2. Big array join (10000 elements, "e0".."e9999" with "," separators).
    // Expected length: 20 + 270 + 3600 + 45000 (element payloads) + 9999
    // (separators) = 58889. Previously crashed with 0xC0000374.
    loc3 = reinterpret_cast<Il2CppObject*>(RefArr(10000));
    {
        // 2026-09-23: 1MB segments make the 80KB array a MOVABLE Gen0 object,
        // so a raw 'big' local (unrooted) goes stale when a threshold
        // collection fires mid-fill; derive the array from the rooted loc3
        // immediately before every store (the allocating calls above may
        // collect and move the array).
        for (int i = 0; i < 10000; ++i)
        {
            Il2CppString* digits = il2cpp::runtime::icall_int32_to_string(i);
            Il2CppString* joined = il2cpp::runtime::il2cpp_string_concat_many(
                2, reinterpret_cast<Il2CppString*>(strE), digits);
            Il2CppArray* big = reinterpret_cast<Il2CppArray*>(loc3);
            SetElem(big, i, reinterpret_cast<Il2CppObject*>(joined));
        }
        Expect(reinterpret_cast<Il2CppArray*>(loc3)->length == 10000, "10000-element string[] built");
    }

    loc4 = il2cpp::runtime::icall_string_join(strComma, loc3);
    {
        Il2CppString* rs = AsString(loc4);
        Expect(rs->length == 58889, "big join length == 58889");
        Expect(rs->chars[0] == u'e' && rs->chars[1] == u'0',
               "big join head chars == \"e0\"");
    }

    // 3. Empty-separator join of the same array: 48890 = element payloads only.
    {
        Il2CppString* rs = AsString(il2cpp::runtime::icall_string_join(Obj(""), loc3));
        Expect(rs->length == 48890, "empty-sep join length == 48890");
    }

    // 4. StringBuilder round trip (10000 iterations, grew past the 64KB
    // buffer threshold: previously corrupted the heap at capacity 32768).
    loc5 = il2cpp_object_new(il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_default(loc5);
    for (int i = 0; i < 10000; ++i)
    {
        if (i > 0) { loc5 = il2cpp::runtime::icall_stringbuilder_append_c(loc5, u','); }
        loc5 = il2cpp::runtime::icall_stringbuilder_append_c(loc5, u'e');
        loc5 = il2cpp::runtime::icall_stringbuilder_append_i(loc5, i);
    }
    {
        Il2CppString* rs = AsString(il2cpp::runtime::icall_stringbuilder_to_string(loc5));
        Expect(rs->length == 58889, "StringBuilder round trip length == 58889");
    }

    // 5. Object[] join: boxed int / string / null / boxed bool.
    {
        Il2CppArray* arr = RefArr(4);
        SetElem(arr, 0, il2cpp_box_int32(nullptr, 1LL));
        SetElem(arr, 1, Obj("x"));
        SetElem(arr, 2, nullptr);
        SetElem(arr, 3, il2cpp::runtime::icall_box_bool(true));
        Il2CppString* r = AsString(il2cpp::runtime::icall_string_join(Obj(","), reinterpret_cast<Il2CppObject*>(arr)));
        Expect(r->length == 9 && WideEqual(r, u"1,x,,True"),
               "object[] join == \"1,x,,True\"");
    }

    // 6. Concat convenience: int / bool with a leading string.
    {
        Il2CppString* r = AsString(il2cpp::runtime::icall_string_concat_int(
            Obj("n="), il2cpp_unbox_int32(il2cpp_box_int32(nullptr, 42LL))));
        Expect(WideEqual(r, u"n=42"), "concat_int \"n=42\"");
    }

    il2cpp::il2cpp_frame_pop(&__frame);

    if (g_failures == 0)
    {
        std::printf("all runtime strjoin tests passed\n");
        return 0;
    }
    std::printf("%d runtime strjoin assertion(s) failed\n", g_failures);
    return 1;
}
