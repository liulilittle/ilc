//------------------------------------------------------------------------------
 // test_string_ascii.cpp -- ASCII fast-path helpers unit tests.
 //------------------------------------------------------------------------------
 #include "il2cpp_string.h"
 #include <cstdio>
 #include <cstring>
 namespace {
 int g_failures = 0;
 void Expect(bool c, const char* w) { if (!c) { std::printf("FAIL: %s\n", w); ++g_failures; } }
 }
 int main() {
     // null handling
     Expect(!il2cpp::il2cpp_string_is_ascii_bulk(nullptr), "bulk null false");
     Expect(il2cpp::il2cpp_string_find_non_ascii(nullptr)==-1, "find null -1");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(nullptr)==0, "prefix null 0");
     Expect(il2cpp::il2cpp_string_count_non_ascii(nullptr)==0, "count null 0");
     Expect(il2cpp::il2cpp_string_count_ascii_lower(nullptr)==0, "count lower null 0");
     Expect(il2cpp::il2cpp_string_count_ascii_upper(nullptr)==0, "count upper null 0");
     Expect(il2cpp::il2cpp_string_is_ascii_upper_noop(nullptr), "upper noop null true");
     Expect(il2cpp::il2cpp_string_is_ascii_lower_noop(nullptr), "lower noop null true");
     Expect(il2cpp::il2cpp_string_is_ascii(nullptr)==false, "is_ascii null false");
     Expect(il2cpp::il2cpp_string_is_ascii_slow(nullptr)==false, "is_ascii_slow null false");

     // empty
     il2cpp::Il2CppString* e = il2cpp::il2cpp_string_new(nullptr, 0);
     Expect(il2cpp::il2cpp_string_is_ascii_bulk(e), "bulk empty true");
     Expect(il2cpp::il2cpp_string_is_ascii(e), "is_ascii empty true");
     Expect(il2cpp::il2cpp_string_find_non_ascii(e)==-1, "find empty -1");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(e)==0, "prefix empty 0");
     Expect(il2cpp::il2cpp_string_count_non_ascii(e)==0, "count empty 0");
     Expect(il2cpp::il2cpp_string_is_ascii_upper_noop(e), "upper noop empty true");
     Expect(il2cpp::il2cpp_string_is_ascii_lower_noop(e), "lower noop empty true");

     // pure ascii "hello" (8+? test unrolled path with 5 and 16)
     il2cpp::Il2CppString* h = il2cpp::il2cpp_string_new_from_cs("hello");
     Expect(il2cpp::il2cpp_string_is_ascii_bulk(h), "bulk hello true");
     Expect(il2cpp::il2cpp_string_is_ascii(h), "is_ascii hello true");
     Expect(il2cpp::il2cpp_string_find_non_ascii(h)==-1, "find hello -1");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(h)==5, "prefix hello 5");
     Expect(il2cpp::il2cpp_string_count_non_ascii(h)==0, "count hello 0");
     Expect(!il2cpp::il2cpp_string_is_ascii_upper_noop(h), "hello has lower => upper not noop");
     Expect(il2cpp::il2cpp_string_is_ascii_lower_noop(h), "hello lower noop true");

     // 16-char ascii to exercise bulk 8 loop twice
     il2cpp::Il2CppString* a16 = il2cpp::il2cpp_string_new_from_cs("abcdefghABCDEFGH");
     Expect(il2cpp::il2cpp_string_is_ascii_bulk(a16), "bulk 16 true");
     Expect(il2cpp::il2cpp_string_count_ascii_lower(a16)==8, "count lower 8");
     Expect(il2cpp::il2cpp_string_count_ascii_upper(a16)==8, "count upper 8");
     Expect(!il2cpp::il2cpp_string_is_ascii_upper_noop(a16), "16 upper not noop");
     Expect(!il2cpp::il2cpp_string_is_ascii_lower_noop(a16), "16 lower not noop");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(a16)==16, "prefix 16");

     // non-ascii at various positions
     char16_t u1[4] = {u'a', 0x80, u'c', u'd'};
     il2cpp::Il2CppString* s1 = il2cpp::il2cpp_string_new(u1, 4);
     Expect(!il2cpp::il2cpp_string_is_ascii_bulk(s1), "bulk non-ascii false");
     Expect(!il2cpp::il2cpp_string_is_ascii(s1), "is_ascii false");
     Expect(il2cpp::il2cpp_string_find_non_ascii(s1)==1, "find 1");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(s1)==1, "prefix 1");
     Expect(il2cpp::il2cpp_string_count_non_ascii(s1)==1, "count 1");

     char16_t u2[10] = {u'a',u'b',u'c',u'd',u'e',u'f',u'g',u'h',0x80,u'j'};
     il2cpp::Il2CppString* s2 = il2cpp::il2cpp_string_new(u2, 10);
     Expect(il2cpp::il2cpp_string_find_non_ascii(s2)==8, "find 8 bulk boundary");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(s2)==8, "prefix 8");
     Expect(il2cpp::il2cpp_string_count_non_ascii(s2)==1, "count 8 1");

     char16_t u3[9] = {0x800, u'a',u'a',u'a',u'a',u'a',u'a',u'a',u'a'};
     il2cpp::Il2CppString* s3 = il2cpp::il2cpp_string_new(u3, 9);
     Expect(il2cpp::il2cpp_string_find_non_ascii(s3)==0, "find 0 first");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(s3)==0, "prefix 0");
     Expect(il2cpp::il2cpp_string_count_non_ascii(s3)==1, "count 1 at 0");

     // all non-ascii
     char16_t u4[8] = {0x80,0x81,0xFF,0x100,0x800,0x1234,0x80,0x80};
     il2cpp::Il2CppString* s4 = il2cpp::il2cpp_string_new(u4, 8);
     Expect(!il2cpp::il2cpp_string_is_ascii_bulk(s4), "bulk all non-ascii false");
     Expect(il2cpp::il2cpp_string_find_non_ascii(s4)==0, "find all 0");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(s4)==0, "prefix all 0");
     Expect(il2cpp::il2cpp_string_count_non_ascii(s4)==8, "count all 8");

     // upper/lower counts on mixed
     char16_t mix[6] = {u'A',u'a',u'Z',u'z',u'0',0x80};
     il2cpp::Il2CppString* m = il2cpp::il2cpp_string_new(mix, 6);
     Expect(il2cpp::il2cpp_string_count_ascii_lower(m)==2, "count lower 2");
     Expect(il2cpp::il2cpp_string_count_ascii_upper(m)==2, "count upper 2");

     // noop checks on pure upper / pure lower
     il2cpp::Il2CppString* upper = il2cpp::il2cpp_string_new_from_cs("HELLO");
     Expect(il2cpp::il2cpp_string_is_ascii_upper_noop(upper), "HELLO upper noop true");
     Expect(!il2cpp::il2cpp_string_is_ascii_lower_noop(upper), "HELLO lower not noop");
     il2cpp::Il2CppString* lower = il2cpp::il2cpp_string_new_from_cs("hello");
     Expect(!il2cpp::il2cpp_string_is_ascii_upper_noop(lower), "hello upper not noop");
     Expect(il2cpp::il2cpp_string_is_ascii_lower_noop(lower), "hello lower noop true");
     il2cpp::Il2CppString* digits = il2cpp::il2cpp_string_new_from_cs("12345");
     Expect(il2cpp::il2cpp_string_is_ascii_upper_noop(digits), "digits upper noop true");
     Expect(il2cpp::il2cpp_string_is_ascii_lower_noop(digits), "digits lower noop true");

     // long 32 ascii to stress 8x loop 4 iterations
     il2cpp::Il2CppString* l32 = il2cpp::il2cpp_string_new_from_cs("abcdefghijklmnopqrstuvwxyz012345");
     Expect(il2cpp::il2cpp_string_is_ascii_bulk(l32), "bulk 32 true");
     Expect(il2cpp::il2cpp_string_ascii_prefix_length(l32)==32, "prefix 32");

     if (g_failures) { std::printf("%d ascii test(s) failed\n", g_failures); return 1; }
     std::printf("all string_ascii tests passed\n"); return 0;
 }
