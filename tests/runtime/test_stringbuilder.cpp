//------------------------------------------------------------------------------
// test_stringbuilder.cpp -- System.Text.StringBuilder runtime unit tests.
//
// Coverage (normal + edge + error paths):
//   1. ctor_default: length 0, capacity 16 (net48 DefaultCapacity), a
//      non-null managed char buffer holding exactly 'capacity' units.
//   2. ctor_capacity: (4) -> capacity 4; (0) -> capacity 16 (net48 treats 0
//      as the default capacity, empirically verified); (-1) -> AoORE.
//   3. ctor_string: "abc" -> length 3, payload "abc", capacity 16; a 20-unit
//      value -> length 20 AND capacity 20 (net48 preallocates
//      max(16, value.Length) -- verified empirically: 20 units -> 20, NOT
//      the 32 the Append growth path would produce); null -> empty.
//   4. append_s: content, chaining (Append returns the same builder), null
//      no-op (same instance), growth keeps the old content intact (net48
//      Grow policy: newCapacity = max(required, capacity * 2) -- 4 + 10
//      units -> 10).
//   5. append_c: single/multiple chars, growth via repeated char appends
//      (2 units + 1 more -> 4: max(3, 2 * 2)).
//   6. append_i: invariant decimal formatting -- 0, positive, negative,
//      int.MaxValue, int.MinValue ("-2147483648"), chained appends.
//   7. append_o: net48 semantics -- a string value is appended via its
//      payload; null is a no-op; a boxed Int32 appends its invariant decimal;
//      a non-string managed object (an array) appends its type's full name
//      (the icall_object_to_string ToString dispatch, valuesemantics.cpp).
//   8. insert: middle/start/end insertion, null no-op, AoORE on negative or
//      past-end index, growth on insert (net48 chunk policy: capacity
//      becomes oldCapacity + value.Length -- capacity 2 + "abcde" -> 7).
//   9. remove: middle/start/end removal, remove-all -> empty, AoORE on the
//      four .NET violation shapes (start < 0, start > Length,
//      length < 0, start > Length - length).
//  10. replace_s_s / replace_c_c: same-width / grow / shrink / multi-occurrence
//      replace, null newValue removes, null/empty oldValue error paths, no-match
//      no-op, and the net48-exact growth policy (capacity = oldCapacity +
//      (newLen - oldLen) * count on a net grow; capacity kept on shrink).
//  11. to_string: content snapshot (mutating the builder afterwards does
//      NOT change an earlier result), empty builder -> "".
//  12. get_length: 0, after appends; null receiver -> NRE.
//  13. GC safety: a rooted builder survives a collection cycle with its
//      content AND its char buffer intact -- proves the refFieldBitmap
//      scans the 'chars' reference field (slot 2).
//  14. Null receiver -> NullReferenceException on every entry point.
//  15. String.Format family (icall_string_format_0/1/2/3 + the params
//      icall_string_format): net48 semantics through the AppendFormatCore
//      kernel -- plain text / "{N}" expansion / "{{}}"-escaping; boxed
//      Int32 invariant decimal, boxed Double / string args, null arg ->
//      empty; params object[] (incl. empty array + plain format); null
//      format -> ArgumentNullException, malformed placeholder / out-of-range
//      index -> FormatException, "{N,width:fmt}" alignment/format
//      specifiers -> NotSupportedException (documented deviation), null
//      args array -> ArgumentNullException.
//  16. Brace-combination grammar (D-SB-1, net48 oracle): the "{{"/"}}" escape
//      pairs combine with "{N}" holes -- "{{0}}"->"{0}", "{{{0}}}"->"{7}",
//      "{0}"->"7", "x{{{0}}}y"->"x{7}y", "{{{{0}}}}"->"{{0}}", "{0}}}"->"7}",
//      "abc{0}x"->"abc7x", "{{{0}}}{{1}}"->"{7}{1}" via BOTH the Format icall
//      path and the AppendFormat icall path (icall_stringbuilder_append_format_
//      1/2/3); net48 FormatException faces "{0}}"/"{{0}}}"/"{0}}}}" locked.
//
// Plain executable + minimal assertion harness (see test_string.cpp): any
// failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_stringbuilder.h"

#include "il2cpp_boxing_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "stringbuilder test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Expect 'obj' (a managed string) to hold exactly the ASCII literal 'ascii'.
void ExpectString(il2cpp::Il2CppObject* obj, const char* ascii, const char* what)
{
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (static_cast<size_t>(s->length) != len)
    {
        Expect(false, what);
        return;
    }
    bool match = true;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            match = false;
            break;
        }
    }
    Expect(match, what);
}

// Run 'fn' expecting it to throw a managed exception whose TYPE NAME (the
// carrier's full "{Type},{Base}" chain) equals 'typeChain'.
template <typename Fn>
void ExpectThrows(Fn fn, const char* typeChain, const char* what)
{
    bool matched = false;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        matched = (std::strcmp(e.type_name(), typeChain) == 0);
    }
    Expect(matched, what);
}

// Allocate a fresh StringBuilder via the generated-code shape:
// il2cpp_object_new(klass) then the ctor icall.
il2cpp::Il2CppStringBuilder* NewDefault()
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_default(sb);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

il2cpp::Il2CppStringBuilder* NewCapacity(int32_t capacity)
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_capacity(sb, capacity);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

il2cpp::Il2CppStringBuilder* NewString(il2cpp::Il2CppObject* value)
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_string(sb, value);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppStringBuilder;
    using il2cpp::runtime::icall_stringbuilder_ctor_default;
    using il2cpp::runtime::icall_stringbuilder_ctor_capacity;
    using il2cpp::runtime::icall_stringbuilder_ctor_string;
    using il2cpp::runtime::icall_stringbuilder_append_s;
    using il2cpp::runtime::icall_stringbuilder_append_c;
    using il2cpp::runtime::icall_stringbuilder_append_i;
using il2cpp::runtime::icall_stringbuilder_append_i_w;
using il2cpp::runtime::icall_stringbuilder_append_i_f;
using il2cpp::runtime::icall_stringbuilder_append_i_wf;
using il2cpp::runtime::icall_stringbuilder_append_s_w;
using il2cpp::runtime::icall_stringbuilder_append_l_w;
using il2cpp::runtime::icall_stringbuilder_append_b_w;
using il2cpp::runtime::icall_stringbuilder_append_c_w;
using il2cpp::runtime::icall_stringbuilder_append_d_w;
    using il2cpp::runtime::icall_stringbuilder_append_o;
    using il2cpp::runtime::icall_stringbuilder_insert;
    using il2cpp::runtime::icall_stringbuilder_remove;
    using il2cpp::runtime::icall_stringbuilder_replace_s_s;
    using il2cpp::runtime::icall_stringbuilder_replace_c_c;
    using il2cpp::runtime::icall_stringbuilder_to_string;
    using il2cpp::runtime::icall_stringbuilder_get_length;
    using il2cpp::runtime::icall_string_format_0;
    using il2cpp::runtime::icall_string_format_1;
    using il2cpp::runtime::icall_string_format_2;
    using il2cpp::runtime::icall_string_format_3;
    using il2cpp::runtime::icall_string_format;
    using il2cpp::runtime::icall_stringbuilder_append_format_1;
    using il2cpp::runtime::icall_stringbuilder_append_format_2;
    using il2cpp::runtime::icall_stringbuilder_append_format_3;

    const char* kAoore = "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
    const char* kNre = "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";
    const char* kAne = "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
    const char* kAe = "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
    const char* kFe = "System.FormatException,System.FormatException,System.SystemException,System.Exception";

    // --- 1. ctor_default ---
    Il2CppStringBuilder* d = NewDefault();
    Expect(d->length == 0, "default ctor length is 0");
    Expect(d->capacity == 16, "default ctor capacity is 16 (net48 DefaultCapacity)");
    Expect(d->chars != nullptr, "default ctor allocates a managed char buffer");
    Expect(d->chars->length == d->capacity, "char buffer length equals the capacity");
    Expect(d->chars->elemSize == 2 && !d->chars->isRefElem,
           "char buffer stores char16_t units (elemSize 2, non-reference elements)");

    // --- 2. ctor_capacity ---
    Il2CppStringBuilder* c4 = NewCapacity(4);
    Expect(c4->capacity == 4 && c4->length == 0, "ctor(4) capacity is 4, length 0");
    Il2CppStringBuilder* c0 = NewCapacity(0);
    Expect(c0->capacity == 16, "ctor(0) falls back to the default capacity (net48 baseline)");
    ExpectThrows([] { (void)NewCapacity(-1); }, kAoore, "ctor(-1) throws ArgumentOutOfRangeException");

    // --- 3. ctor_string ---
    Il2CppStringBuilder* cs = NewString(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")));
    Expect(cs->length == 3, "ctor(\"abc\") length is 3");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(cs)), "abc",
                 "ctor(\"abc\") content is \"abc\"");
    Expect(cs->capacity == 16, "ctor(\"abc\") capacity stays at the default 16");
    Il2CppStringBuilder* cnull = NewString(nullptr);
    Expect(cnull->length == 0, "ctor(null) yields an empty builder");
    Il2CppStringBuilder* clong = NewString(reinterpret_cast<Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("01234567890123456789")));
    Expect(clong->length == 20, "ctor(20-unit string) length is 20");
    Expect(clong->capacity == 20, "ctor grows EXACTLY to the required capacity (net48 baseline: 20)");

    // --- 4. append_s ---
    Il2CppStringBuilder* a = NewDefault();
    Il2CppObject* ret = icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(a),
                                                     reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")));
    Expect(ret == reinterpret_cast<Il2CppObject*>(a), "Append returns the SAME builder (chaining)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(a)), "abc",
                 "append_s fills the content");
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(a),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("def")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(a)), "abcdef",
                 "chained append_s concatenates");
    Il2CppObject* noopRet = icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(a), nullptr);
    Expect(noopRet == reinterpret_cast<Il2CppObject*>(a), "append_s(null) is a no-op on the same instance");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(a)), "abcdef",
                 "append_s(null) leaves the content unchanged");
    Il2CppStringBuilder* grow = NewCapacity(4);
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(grow),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("0123456789")));
    Expect(grow->length == 10 && grow->capacity == 10,
           "append_s growth is exact: capacity 4 -> 10 (net48 baseline)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(grow)), "0123456789",
                 "append_s growth keeps the content intact");

    // --- 5. append_c ---
    Il2CppStringBuilder* ac = NewDefault();
    (void)icall_stringbuilder_append_c(reinterpret_cast<Il2CppObject*>(ac), u'a');
    (void)icall_stringbuilder_append_c(reinterpret_cast<Il2CppObject*>(ac), u'b');
    (void)icall_stringbuilder_append_c(reinterpret_cast<Il2CppObject*>(ac), u'c');
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ac)), "abc",
                 "repeated append_c builds the content");
    Il2CppStringBuilder* acg = NewCapacity(2);
    (void)icall_stringbuilder_append_c(reinterpret_cast<Il2CppObject*>(acg), u'x');
    (void)icall_stringbuilder_append_c(reinterpret_cast<Il2CppObject*>(acg), u'y');
    (void)icall_stringbuilder_append_c(reinterpret_cast<Il2CppObject*>(acg), u'z');
    Expect(acg->length == 3 && acg->capacity == 4, "append_c growth follows the Grow policy: capacity 2 -> 4 (net48 baseline)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(acg)), "xyz",
                 "append_c after growth keeps the content");

    // --- 6. append_i ---
    Il2CppStringBuilder* ai = NewDefault();
    (void)icall_stringbuilder_append_i(reinterpret_cast<Il2CppObject*>(ai), 0);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ai)), "0",
                 "append_i(0) formats \"0\"");
    (void)icall_stringbuilder_append_i(reinterpret_cast<Il2CppObject*>(ai), 42);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ai)), "042",
                 "append_i(42) formats \"42\"");
    (void)icall_stringbuilder_append_i(reinterpret_cast<Il2CppObject*>(ai), -12345);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ai)), "042-12345",
                 "append_i(-12345) formats \"-12345\"");
    (void)icall_stringbuilder_append_i(reinterpret_cast<Il2CppObject*>(ai), 2147483647);
    (void)icall_stringbuilder_append_i(reinterpret_cast<Il2CppObject*>(ai), INT32_MIN);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ai)),
                 "042-123452147483647-2147483648",
                 "append_i handles int.MaxValue / int.MinValue without overflow");

    // --- 7. append_o ---
    Il2CppStringBuilder* ao = NewDefault();
    (void)icall_stringbuilder_append_o(reinterpret_cast<Il2CppObject*>(ao),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("OK")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ao)), "OK",
                 "append_o(string) appends the string payload");
    Il2CppObject* noopO = icall_stringbuilder_append_o(reinterpret_cast<Il2CppObject*>(ao), nullptr);
    Expect(noopO == reinterpret_cast<Il2CppObject*>(ao), "append_o(null) is a no-op on the same instance");
    // net48 Append(object) = Append(value.ToString()): a boxed Int32 appends
    // its invariant decimal, any other object appends its type's full name
    // via the icall_object_to_string dispatch (valuesemantics.cpp).
    (void)icall_stringbuilder_append_o(reinterpret_cast<Il2CppObject*>(ao),
                                       il2cpp::il2cpp_box_int32(nullptr, 42));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ao)), "OK42",
                 "append_o(boxed Int32) appends the invariant decimal");
    (void)icall_stringbuilder_append_o(reinterpret_cast<Il2CppObject*>(ao),
                                       il2cpp::il2cpp_box_int32(nullptr, -7));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ao)), "OK42-7",
                 "append_o(negative boxed Int32) appends the sign");
    il2cpp::Il2CppArray* notAString = il2cpp::il2cpp_array_new(nullptr, 4, false, 4);
    (void)icall_stringbuilder_append_o(reinterpret_cast<Il2CppObject*>(ao),
                                       reinterpret_cast<Il2CppObject*>(notAString));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ao)), "OK42-7[System.Int32",
                 "append_o(non-string object) appends its type's full name (net48 ToString dispatch)");

    // --- 8. insert ---
    Il2CppStringBuilder* ins = NewDefault();
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(ins),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hlo")));
    (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(ins), 1,
                                     reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("el")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ins)), "Hello",
                 "insert at the middle splices the content");
    (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(ins), 0,
                                     reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(">")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ins)), ">Hello",
                 "insert at 0 prepends");
    (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(ins), ins->length,
                                     reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("!")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ins)), ">Hello!",
                 "insert at Length appends");
    Il2CppObject* insNull = icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(ins), 0, nullptr);
    Expect(insNull == reinterpret_cast<Il2CppObject*>(ins), "insert(index, null) is a no-op");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(ins)), ">Hello!",
                 "insert(null) leaves the content unchanged");
    ExpectThrows([ins] { (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(ins), -1,
                                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("x"))); },
                 kAoore, "insert(-1, s) throws ArgumentOutOfRangeException");
    ExpectThrows([ins] { (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(ins), 9999,
                                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("x"))); },
                 kAoore, "insert(> Length, s) throws ArgumentOutOfRangeException");
    Il2CppStringBuilder* insGrow = NewCapacity(2);
    (void)icall_stringbuilder_insert(reinterpret_cast<Il2CppObject*>(insGrow), 0,
                                     reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abcde")));
    Expect(insGrow->length == 5 && insGrow->capacity == 7,
           "insert growth is exact: capacity 2 -> 7 (net48 baseline)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(insGrow)), "abcde",
                 "insert growth keeps the inserted content");

    // --- 9. remove ---
    Il2CppStringBuilder* rem = NewString(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Hello, World!")));
    (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), 5, 2);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rem)), "HelloWorld!",
                 "remove at the middle shifts the tail left");
    (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), 0, 5);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rem)), "World!",
                 "remove from the start drops the prefix");
    (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), rem->length - 1, 1);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rem)), "World",
                 "remove to the end drops the suffix");
    Il2CppStringBuilder* wipe = NewString(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abcdef")));
    (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(wipe), 0, 6);
    Expect(wipe->length == 0, "remove(0, all) leaves length 0");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(wipe)), "",
                 "remove(0, all) yields the empty string");
    ExpectThrows([rem] { (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), -1, 1); }, kAoore,
                 "remove(-1, 1) throws ArgumentOutOfRangeException");
    ExpectThrows([rem] { (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), 0, -1); }, kAoore,
                 "remove(0, -1) throws ArgumentOutOfRangeException");
    ExpectThrows([rem] { (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), 9999, 1); }, kAoore,
                 "remove(> Length, 1) throws ArgumentOutOfRangeException");
    ExpectThrows([rem] { (void)icall_stringbuilder_remove(reinterpret_cast<Il2CppObject*>(rem), 3, 9999); }, kAoore,
                 "remove(3, past-end length) throws ArgumentOutOfRangeException");

    // --- 10. replace_s_s / replace_c_c ---
    // Content: every occurrence, greedy non-overlapping scan, null newValue
    // removes, char replace in place. Capacity (net48 baseline, empirically
    // verified): a net GROWTH sets capacity = oldCapacity + (newLen-oldLen)*
    // count (e.g. capacity 4 + "ab"->"abc" -> 5, capacity 4 + "ab"->
    // "abcdefghij" -> 12); shrink / same-width keeps the current capacity
    // (Remove's "does not reduce the capacity" contract).
    Il2CppStringBuilder* rp = NewString(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abab")));
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rp),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("cd")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rp)), "cdcd",
                 "replace_s_s same-width replaces every occurrence");
    Expect(rp->capacity == 16, "replace_s_s same-width keeps the capacity (16)");
    (void)icall_stringbuilder_replace_c_c(reinterpret_cast<Il2CppObject*>(rp), u'c', u'C');
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rp)), "CdCd",
                 "replace_c_c replaces every matching unit in place");
    // Shrink: "Cd" -> "e" (2 matches, delta -1): capacity kept, length 4.
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rp),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("Cd")),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("e")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rp)), "ee",
                 "replace_s_s shrink removes the matched text");
    Expect(rp->length == 2 && rp->capacity == 16,
           "replace_s_s shrink keeps the capacity (net48: capacity never shrinks on replace)");
    // Grow by exactly one unit: capacity 4 + 1 match * delta 1 -> 5.
    Il2CppStringBuilder* rg1 = NewCapacity(4);
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(rg1),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")));
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rg1),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")));
    Expect(rg1->length == 3 && rg1->capacity == 5,
           "replace_s_s grow capacity is oldCapacity + delta: 4 + 1 -> 5 (net48 baseline)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rg1)), "abc",
                 "replace_s_s grow keeps the replaced content");
    // Grow by a large delta: capacity 4 + 1 match * delta 8 -> 12.
    Il2CppStringBuilder* rg2 = NewCapacity(4);
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(rg2),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")));
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rg2),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abcdefghij")));
    Expect(rg2->length == 10 && rg2->capacity == 12,
           "replace_s_s grow capacity is oldCapacity + delta: 4 + 8 -> 12 (net48 baseline)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rg2)), "abcdefghij",
                 "replace_s_s large grow keeps the replaced content");
    // Multi-occurrence grow: capacity 4, 2 matches of "ab" -> "abcdefgh":
    // delta +6 each -> 4 + 12 = 16.
    Il2CppStringBuilder* rg3 = NewCapacity(4);
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(rg3),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abab")));
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rg3),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abcdefgh")));
    Expect(rg3->length == 16 && rg3->capacity == 16,
           "replace_s_s multi-occurrence grow: 4 + 2*6 -> 16 (net48 baseline)");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rg3)), "abcdefghabcdefgh",
                 "replace_s_s multi-occurrence grow replaces every occurrence");
    // Null newValue removes the occurrences (net48).
    Il2CppStringBuilder* rn = NewString(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("aXbXc")));
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rn),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("X")),
                                          nullptr);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rn)), "abc",
                 "replace_s_s(null newValue) removes the occurrences");
    // Error paths: null oldValue -> ArgumentNullException; empty oldValue ->
    // ArgumentException; no match is a no-op (same capacity).
    ExpectThrows([rp] { (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rp), nullptr,
                                                               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("x"))); },
                 kAne, "replace_s_s(null oldValue) throws ArgumentNullException");
    ExpectThrows([rp] { (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rp),
                                                               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("")),
                                                               reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("x"))); },
                 kAe, "replace_s_s(empty oldValue) throws ArgumentException");
    Il2CppStringBuilder* rnop = NewString(reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("hello")));
    (void)icall_stringbuilder_replace_s_s(reinterpret_cast<Il2CppObject*>(rnop),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("zz")),
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("longer")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rnop)), "hello",
                 "replace_s_s with no match is a no-op");

    // --- 11. to_string ---
    Il2CppStringBuilder* snap = NewDefault();
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(snap),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc")));
    Il2CppObject* first = icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(snap));
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(snap),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("def")));
    ExpectString(first, "abc", "ToString is a snapshot: later appends do not change an earlier result");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(snap)), "abcdef",
                 "ToString reflects the current content");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(NewDefault())), "",
                 "ToString of an empty builder is \"\"");

    // --- 12. get_length ---
    Expect(icall_stringbuilder_get_length(reinterpret_cast<Il2CppObject*>(NewDefault())) == 0,
           "get_length of a fresh builder is 0");
    Expect(icall_stringbuilder_get_length(reinterpret_cast<Il2CppObject*>(snap)) == 6,
           "get_length after appends is 6");

    // --- 13. GC safety: the chars field is scanned through refFieldBitmap ---
    Il2CppStringBuilder* gcSb = NewCapacity(4);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&gcSb));
    (void)icall_stringbuilder_append_s(reinterpret_cast<Il2CppObject*>(gcSb),
                                       reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("0123456789")));
    il2cpp::Il2CppObject* junk = icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(gcSb));
    (void)junk;
    il2cpp::il2cpp_gc_collect();
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(gcSb)), "0123456789",
                 "rooted builder survives a collection with its content intact");
    Expect(gcSb->chars != nullptr && gcSb->chars->length == gcSb->capacity,
           "the char buffer array survived the collection (refFieldBitmap slot 2 scan)");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&gcSb));

    // --- 14. Null receivers ---
    ExpectThrows([] { icall_stringbuilder_ctor_default(nullptr); }, kNre,
                 "ctor_default on a null receiver throws NullReferenceException");
    ExpectThrows([] { icall_stringbuilder_ctor_capacity(nullptr, 4); }, kNre,
                 "ctor_capacity on a null receiver throws NullReferenceException");
    ExpectThrows([] { icall_stringbuilder_ctor_string(nullptr, nullptr); }, kNre,
                 "ctor_string on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_append_s(nullptr, nullptr); }, kNre,
                 "append_s on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_append_c(nullptr, u'a'); }, kNre,
                 "append_c on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_append_i(nullptr, 1); }, kNre,
                 "append_i on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_append_o(nullptr, nullptr); }, kNre,
                 "append_o on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_insert(nullptr, 0, nullptr); }, kNre,
                 "insert on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_remove(nullptr, 0, 0); }, kNre,
                 "remove on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_to_string(nullptr); }, kNre,
                 "ToString on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_get_length(nullptr); }, kNre,
                 "get_length on a null receiver throws NullReferenceException");

    // --- 15. String.Format family (icall_string_format_0/1/2/3 + params) ---
    ExpectString(icall_string_format_0(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("plain"))),
                 "plain", "Format(\"plain\") returns the literal text");
    ExpectString(icall_string_format_0(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs(""))),
                 "", "Format(\"\") returns the empty string");
    ExpectThrows([&] {
        (void)icall_string_format_0(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0}")));
    }, kFe, "Format(\"{0}\") with no args throws FormatException (index out of range)");

    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("v={0}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42)),
                 "v=42", "Format(\"v={0}\", boxed 42) expands the Int32 invariantly");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("v={0}")),
                 il2cpp::runtime::icall_box_double(3.5)),
                 "v=3.5", "Format(\"v={0}\", boxed 3.5) formats via icall_object_to_string");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0}")),
                 reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("abc"))),
                 "abc", "Format(\"{0}\", string) appends the string payload");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("a{0}b")),
                 nullptr),
                 "ab", "Format(\"a{0}b\", null) appends nothing (net48 String.Empty)");

    ExpectString(icall_string_format_2(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0}-{1}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42),
                 il2cpp::il2cpp_box_int32(nullptr, 7)),
                 "42-7", "Format(\"{0}-{1}\", 42, 7) expands both placeholders");
    ExpectString(icall_string_format_3(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0}{1}{2}")),
                 reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("a")),
                 reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("b")),
                 reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("c"))),
                 "abc", "Format(\"{0}{1}{2}\", a, b, c) concatenates the three args");

    // --- params object[] form (the icall reads arr->data payload) ---
    il2cpp::Il2CppArray* fmtArgs = il2cpp::il2cpp_array_new(
        il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 2);
    reinterpret_cast<Il2CppObject**>(fmtArgs->data)[0] = il2cpp::il2cpp_box_int32(nullptr, 42);
    reinterpret_cast<Il2CppObject**>(fmtArgs->data)[1] = il2cpp::il2cpp_box_int32(nullptr, 7);
    ExpectString(icall_string_format(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0}-{1}")),
                 reinterpret_cast<Il2CppObject*>(fmtArgs)),
                 "42-7", "Format(\"{0}-{1}\", params object[]{42, 7}) expands the array payload");
    il2cpp::Il2CppArray* fmtEmpty = il2cpp::il2cpp_array_new(
        il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 0);
    ExpectString(icall_string_format(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("plain")),
                 reinterpret_cast<Il2CppObject*>(fmtEmpty)),
                 "plain", "Format(\"plain\", empty params array) is legal (net48)");
    ExpectThrows([&] {
        (void)icall_string_format(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("plain")),
            nullptr);
    }, kAne, "Format(format, null args array) throws ArgumentNullException");

    // --- escaping: "{{" / "}}" are literal braces (net48) ---
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{{0}}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42)),
                 "{0}", "Format(\"{{0}}\") escapes to a literal \"{0}\" (no expansion)");

    // --- brace-combination grammar (D-SB-1, net48 oracle on this host) ---
    // net48 AppendFormat: "{{0}}" -> "{0}", "{{{0}}}" -> "{7}", "{0}" -> "7".
    // The escaped-brace scanner consumes a leading "{{" pair as a literal
    // brace, then expands the "{0}" placeholder, then consumes the trailing
    // "}}" pair -- the combination never leaves a lone closing brace, so the
    // "}}" escape path (stringbuilder.cpp:490) always absorbs it (the original
    // D-SB-1 report traced a phantom lone '}' that cannot occur in this
    // 7-unit input).
    {
        Il2CppObject* box7 = il2cpp::il2cpp_box_int32(nullptr, 7);
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{0}}")),
                     box7),
                     "{0}", "Format(\"{{0}}\", 7) -> literal \"{0}\" (arg ignored)");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{{0}}}")),
                     box7),
                     "{7}", "Format(\"{{{0}}}\", 7) -> \"{7}\" (escape + hole + escape)");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{0}")),
                     box7),
                     "7", "Format(\"{0}\", 7) -> plain expansion \"7\"");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("x{{{0}}}y")),
                     box7),
                     "x{7}y", "Format(\"x{{{0}}}y\", 7) -> \"x{7}y\"");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{{{0}}}}")),
                     box7),
                     "{{0}}", "Format(\"{{{{0}}}}\", 7) -> \"{{0}}\" (two escape pairs)");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{0}}}")),
                     box7),
                     "7}", "Format(\"{0}}}\", 7) -> \"7}\" (escape pair after hole)");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{")),
                     box7),
                     "{", "Format(\"{{}\") -> lone \"{\"");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("}}")),
                     box7),
                     "}", "Format(\"}}\") -> lone \"}\"");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{0}}{0}")),
                     il2cpp::il2cpp_box_int32(nullptr, 9)),
                     "{0}9", "Format(\"{{0}}{0}\", 9) -> escaped \"{0}\" then expansion \"9\"");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("abc{0}x")),
                     box7),
                     "abc7x", "Format(\"abc{0}x\", 7) -> hole with trailing literal \"abc7x\"");
        ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{0}xyz")),
                     box7),
                     "7xyz", "Format(\"{0}xyz\", 7) -> \"7xyz\"");
        ExpectString(icall_string_format_2(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{0}}{1}")),
                     il2cpp::il2cpp_box_int32(nullptr, 9),
                     il2cpp::il2cpp_box_int32(nullptr, 8)),
                     "{0}8", "Format(\"{{0}}{1}\", 9, 8) -> escaped \"{0}\" then arg1 \"8\"");
        ExpectString(icall_string_format_2(reinterpret_cast<Il2CppObject*>(
                         il2cpp::il2cpp_string_new_from_cs("{{{0}}}{{1}}")),
                     box7,
                     il2cpp::il2cpp_box_int32(nullptr, 8)),
                     "{7}{1}", "Format(\"{{{0}}}{{1}}\", 7, 8) -> \"{7}{1}\"");
        ExpectThrows([&] {
            (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                il2cpp::il2cpp_string_new_from_cs("{0}}}}")),
                box7);
        }, kFe, "Format(\"{0}}}}\") throws FormatException (odd trailing escape pair)");
        // net48 rejects a lone closing brace even when a preceding "}}" pair
        // was consumed (the pair leaves no remainder for the placeholder).
        ExpectThrows([&] {
            (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                il2cpp::il2cpp_string_new_from_cs("{0}}")),
                box7);
        }, kFe, "Format(\"{0}}\") throws FormatException (lone closing brace)");
        ExpectThrows([&] {
            (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                il2cpp::il2cpp_string_new_from_cs("{{0}}}")),
                box7);
        }, kFe, "Format(\"{{0}}}\") throws FormatException (three closing braces)");
    }

    // --- AppendFormat brace combinations (the D-SB-1 report path) ---
    {
        Il2CppObject* box7 = il2cpp::il2cpp_box_int32(nullptr, 7);
        Il2CppStringBuilder* af1 = NewDefault();
        (void)icall_stringbuilder_append_format_1(
            reinterpret_cast<Il2CppObject*>(af1),
            reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("{{{0}}}")),
            box7);
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af1)),
                     "{7}", "AppendFormat(\"{{{0}}}\", 7) -> \"{7}\"");
        Il2CppStringBuilder* af2 = NewDefault();
        (void)icall_stringbuilder_append_format_1(
            reinterpret_cast<Il2CppObject*>(af2),
            reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("{{0}}")),
            box7);
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af2)),
                     "{0}", "AppendFormat(\"{{0}}\", 7) -> literal \"{0}\"");
        Il2CppStringBuilder* af3 = NewDefault();
        (void)icall_stringbuilder_append_format_1(
            reinterpret_cast<Il2CppObject*>(af3),
            reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("{0}")),
            box7);
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af3)),
                     "7", "AppendFormat(\"{0}\", 7) -> \"7\"");
        Il2CppStringBuilder* af4 = NewDefault();
        (void)icall_stringbuilder_append_format_2(
            reinterpret_cast<Il2CppObject*>(af4),
            reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("{{{1}}}")),
            il2cpp::il2cpp_box_int32(nullptr, 1), box7);
        ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af4)),
                     "{7}", "AppendFormat(\"{{{1}}}\", 1, 7) -> \"{7}\"");
        ExpectThrows([&] {
            Il2CppStringBuilder* af5 = NewDefault();
            (void)icall_stringbuilder_append_format_1(
                reinterpret_cast<Il2CppObject*>(af5),
                reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("{{0}}}")),
                box7);
        }, kFe, "AppendFormat(\"{{0}}}\") throws FormatException (odd closing braces)");
    }

    // --- error paths: null format / malformed / out-of-range / specifiers ---
    ExpectThrows([&] { (void)icall_string_format_0(nullptr); }, kAne,
                 "Format(null) throws ArgumentNullException");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{1}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{1}\", 42) throws FormatException (index out of range)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("abc{0")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"abc{0\") throws FormatException (unclosed placeholder)");
    ExpectThrows([&] {
        (void)icall_string_format_0(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x}")));
    }, kFe, "Format(\"x}\") throws FormatException (stray closing brace)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{x}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{x}\") throws FormatException (non-digit placeholder)");

    // --- composite specifiers: alignment {N,width} and {N:fmt} (net48) ---
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0,5}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42)),
                 "   42", "Format(\"{0,5}\", 42) right-aligns to width 5");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0,-5}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42)),
                 "42   ", "Format(\"{0,-5}\", 42) left-aligns to width 5");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0,2}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42)),
                 "42", "Format(\"{0,2}\", 42) does not truncate a wider value");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0:f2}")),
                 il2cpp::runtime::icall_box_double(3.14159)),
                 "3.14", "Format(\"{0:f2}\", 3.14159) applies the F2 specifier");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0:x}")),
                 il2cpp::il2cpp_box_int32(nullptr, 255)),
                 "ff", "Format(\"{0:x}\", 255) applies the lowercase hex specifier");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0:X}")),
                 il2cpp::il2cpp_box_int32(nullptr, 255)),
                 "FF", "Format(\"{0:X}\", 255) applies the uppercase hex specifier");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0,8:f2}")),
                 il2cpp::runtime::icall_box_double(3.14159)),
                 "    3.14", "Format(\"{0,8:f2}\", 3.14159) aligns the formatted text");
    ExpectString(icall_string_format_1(reinterpret_cast<Il2CppObject*>(
                     il2cpp::il2cpp_string_new_from_cs("{0:}")),
                 il2cpp::il2cpp_box_int32(nullptr, 42)),
                 "42", "Format(\"{0:}\", 42) treats an empty specifier as default");

    // --- composite error paths (net48 FormatException) ---
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0,}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{0,}\") throws FormatException (empty alignment)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0,-}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{0,-}\") throws FormatException (sign without digits)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0,+5}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{0,+5}\") throws FormatException (net48 rejects '+' alignment)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0,2147483648}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{0,2147483648}\") throws FormatException (alignment overflow)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0,5")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{0,5\") throws FormatException (unclosed after alignment)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{0:f2")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{0:f2\") throws FormatException (unclosed format string)");
    ExpectThrows([&] {
        (void)icall_string_format_1(reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("{,5}")),
            il2cpp::il2cpp_box_int32(nullptr, 42));
    }, kFe, "Format(\"{,5}\") throws FormatException (empty index)");

    // --- 17. AppendFormatted alignment/format trios (P25g: dhprobe -- the
    // net6+ DefaultInterpolatedStringHandler '{x,5}' / '{x:D3}' / '{x,5:D3}'
    // overloads). Each trio entry delegates to icall_format_composite_item
    // (the SAME net48 composite engine the section-15 tests pin), so these
    // assertions verify the value-type dispatch + boxing only.
    Il2CppStringBuilder* af = NewDefault();
    (void)icall_stringbuilder_append_i_w(reinterpret_cast<Il2CppObject*>(af), 42, 5);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   42",
                 "append_i_w(42, 5) right-aligns to width 5");
    (void)icall_stringbuilder_append_i_w(reinterpret_cast<Il2CppObject*>(af), 42, -5);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   ",
                 "append_i_w(42, -5) left-aligns (negative width)");
    (void)icall_stringbuilder_append_i_f(reinterpret_cast<Il2CppObject*>(af), 5,
                                         reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("D3")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   005",
                 "append_i_f(5, \"D3\") applies the D3 specifier");
    (void)icall_stringbuilder_append_i_wf(reinterpret_cast<Il2CppObject*>(af), 5, 3,
                                          reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("D3")));
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   005005",
                 "append_i_wf(5, 3, \"D3\") width + format combine");
    (void)icall_stringbuilder_append_s_w(reinterpret_cast<Il2CppObject*>(af),
                                         reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ab")), 4);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   005005  ab",
                 "append_s_w(\"ab\", 4) right-aligns the string payload");
    (void)icall_stringbuilder_append_l_w(reinterpret_cast<Il2CppObject*>(af), -12, 5);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   005005  ab  -12",
                 "append_l_w(-12, 5) right-aligns the int64 payload");
    (void)icall_stringbuilder_append_b_w(reinterpret_cast<Il2CppObject*>(af), true, 6);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   005005  ab  -12  True",
                 "append_b_w(true, 6) right-aligns capitalized Boolean text");
    (void)icall_stringbuilder_append_c_w(reinterpret_cast<Il2CppObject*>(af), 65, 3);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)), "   4242   005005  ab  -12  True  A",
                 "append_c_w(L'A', 3) right-aligns the char");
    (void)icall_stringbuilder_append_d_w(reinterpret_cast<Il2CppObject*>(af), 1.5, 6);
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(af)),
                 "   4242   005005  ab  -12  True  A   1.5",
                 "append_d_w(1.5, 6) right-aligns the double payload");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d stringbuilder assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all stringbuilder tests passed\n");
    return 0;
}
