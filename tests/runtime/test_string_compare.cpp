//------------------------------------------------------------------------------
 // test_string_compare.cpp -- string_compare bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "string_compare.h"
 #include <cstdio>
 namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } }
 int main(){
     // equals ordinal
     Expect(il2cpp::il2cpp_string_equals_ordinal(nullptr,nullptr),"equals null null true");
     Expect(!il2cpp::il2cpp_string_equals_ordinal(nullptr, (il2cpp::Il2CppString*)0x1),"equals null non false");
     il2cpp::Il2CppString* e = il2cpp::il2cpp_string_new(nullptr,0);
     il2cpp::Il2CppString* e2 = il2cpp::il2cpp_string_new(nullptr,0);
     Expect(il2cpp::il2cpp_string_equals_ordinal(e,e2),"equals empty true");
     il2cpp::Il2CppString* a = il2cpp::il2cpp_string_new_from_cs("hello");
     il2cpp::Il2CppString* b = il2cpp::il2cpp_string_new_from_cs("hello");
     il2cpp::Il2CppString* c = il2cpp::il2cpp_string_new_from_cs("HELLO");
     il2cpp::Il2CppString* d = il2cpp::il2cpp_string_new_from_cs("hell");
     Expect(il2cpp::il2cpp_string_equals_ordinal(a,b),"equals hello true");
     Expect(!il2cpp::il2cpp_string_equals_ordinal(a,c),"equals case false");
     Expect(!il2cpp::il2cpp_string_equals_ordinal(a,d),"equals length false");
     Expect(il2cpp::il2cpp_string_equals_ordinal(a,a),"equals same true");
     // 16-len
     il2cpp::Il2CppString* s16 = il2cpp::il2cpp_string_new_from_cs("abcdefghABCDEFGH");
     il2cpp::Il2CppString* s16c = il2cpp::il2cpp_string_new_from_cs("abcdefghABCDEFGH");
     Expect(il2cpp::il2cpp_string_equals_ordinal(s16,s16c),"equals 16 true");
     il2cpp::Il2CppString* s16d = il2cpp::il2cpp_string_new_from_cs("abcdefghABCDEFGX");
     Expect(!il2cpp::il2cpp_string_equals_ordinal(s16,s16d),"equals 16 false tail");
     // equals ignore case
     Expect(il2cpp::il2cpp_string_equals_ordinal_ignore_case(a,c),"equals ignore case true");
     Expect(il2cpp::il2cpp_string_equals_ordinal_ignore_case(c,a),"equals ignore reverse true");
     Expect(!il2cpp::il2cpp_string_equals_ordinal_ignore_case(a,d),"equals ignore len false");
     il2cpp::Il2CppString* mix = il2cpp::il2cpp_string_new_from_cs("HeLLo");
     Expect(il2cpp::il2cpp_string_equals_ordinal_ignore_case(a,mix),"equals ignore HeLLo true");
     // compare ordinal
     Expect(il2cpp::il2cpp_string_compare_ordinal(nullptr,nullptr)==0,"cmp null null 0");
     Expect(il2cpp::il2cpp_string_compare_ordinal(nullptr,a)<0,"cmp null < a");
     Expect(il2cpp::il2cpp_string_compare_ordinal(a,nullptr)>0,"cmp a > null");
     Expect(il2cpp::il2cpp_string_compare_ordinal(a,b)==0,"cmp hello 0");
     Expect(il2cpp::il2cpp_string_compare_ordinal(a,d)>0,"cmp hello > hell");
     Expect(il2cpp::il2cpp_string_compare_ordinal(d,a)<0,"cmp hell < hello");
     il2cpp::Il2CppString* e1 = il2cpp::il2cpp_string_new_from_cs("abc");
     il2cpp::Il2CppString* e2b = il2cpp::il2cpp_string_new_from_cs("abd");
     Expect(il2cpp::il2cpp_string_compare_ordinal(e1,e2b)<0,"cmp abc < abd");
     Expect(il2cpp::il2cpp_string_compare_ordinal(e2b,e1)>0,"cmp abd > abc");
     // 8x bulk compare
     il2cpp::Il2CppString* s32a = il2cpp::il2cpp_string_new_from_cs("abcdefghijklmnopqrstuvwxyz012345");
     il2cpp::Il2CppString* s32b = il2cpp::il2cpp_string_new_from_cs("abcdefghijklmnopqrstuvwxyz012345");
     Expect(il2cpp::il2cpp_string_compare_ordinal(s32a,s32b)==0,"cmp 32 0");
     il2cpp::Il2CppString* s32c = il2cpp::il2cpp_string_new_from_cs("abcdefghijklmnopqrstuvwxyz012346");
     Expect(il2cpp::il2cpp_string_compare_ordinal(s32a,s32c)<0,"cmp 32 <");
     // compare ignore case
     Expect(il2cpp::il2cpp_string_compare_ordinal_ignore_case(a,c)==0,"cmp ignore 0");
     Expect(il2cpp::il2cpp_string_compare_ordinal_ignore_case(a,mix)==0,"cmp ignore HeLLo 0");
     il2cpp::Il2CppString* lo = il2cpp::il2cpp_string_new_from_cs("abc");
     il2cpp::Il2CppString* hi = il2cpp::il2cpp_string_new_from_cs("ABD");
     Expect(il2cpp::il2cpp_string_compare_ordinal_ignore_case(lo,hi)<0,"cmp ignore abc < ABD");
     Expect(il2cpp::il2cpp_string_compare_ordinal_ignore_case(hi,lo)>0,"cmp ignore ABD > abc");
     if(g_failures){ std::printf("%d string_compare test(s) failed\n",g_failures); return 1; }
     std::printf("all string_compare tests passed\n"); return 0;
 }
