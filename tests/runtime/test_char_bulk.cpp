//------------------------------------------------------------------------------
 // test_char_bulk.cpp -- char_bulk bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "char_bulk.h"
 #include <cstdio>
 namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } }
 int main(){
     Expect(!il2cpp::il2cpp_char_all_digit(nullptr),"all digit null false");
     Expect(!il2cpp::il2cpp_char_all_digit(il2cpp::il2cpp_string_new(nullptr,0)),"all digit empty false");
     il2cpp::Il2CppString* d = il2cpp::il2cpp_string_new_from_cs("0123456789");
     Expect(il2cpp::il2cpp_char_all_digit(d),"all digit true 10");
     il2cpp::Il2CppString* d16 = il2cpp::il2cpp_string_new_from_cs("0123456789ABCDEF"); // 16 but has letters
     Expect(!il2cpp::il2cpp_char_all_digit(d16),"all digit 16 false");
     il2cpp::Il2CppString* d8 = il2cpp::il2cpp_string_new_from_cs("12345678");
     Expect(il2cpp::il2cpp_char_all_digit(d8),"all digit 8 true");
     il2cpp::Il2CppString* d9 = il2cpp::il2cpp_string_new_from_cs("123456789");
     Expect(il2cpp::il2cpp_char_all_digit(d9),"all digit 9 true");
     il2cpp::Il2CppString* nd = il2cpp::il2cpp_string_new_from_cs("12a45");
     Expect(!il2cpp::il2cpp_char_all_digit(nd),"all digit false with letter");

     il2cpp::Il2CppString* l = il2cpp::il2cpp_string_new_from_cs("AbCdEfGh");
     Expect(il2cpp::il2cpp_char_all_letter(l),"all letter true 8");
     Expect(!il2cpp::il2cpp_char_all_letter(d),"all letter digit false");
     il2cpp::Il2CppString* ld = il2cpp::il2cpp_string_new_from_cs("Ab1");
     Expect(!il2cpp::il2cpp_char_all_letter(ld),"all letter false digit");
     il2cpp::Il2CppString* l16 = il2cpp::il2cpp_string_new_from_cs("abcdefghABCDEFGH");
     Expect(il2cpp::il2cpp_char_all_letter(l16),"all letter 16 true");

     il2cpp::Il2CppString* lod = il2cpp::il2cpp_string_new_from_cs("aB3");
     Expect(il2cpp::il2cpp_char_all_letter_or_digit(lod),"all lod true");
     il2cpp::Il2CppString* notlod = il2cpp::il2cpp_string_new_from_cs("aB-");
     Expect(!il2cpp::il2cpp_char_all_letter_or_digit(notlod),"all lod false");

     Expect(il2cpp::il2cpp_char_count_digit(d)==10,"count digit 10");
     Expect(il2cpp::il2cpp_char_count_digit(l)==0,"count digit 0");
     il2cpp::Il2CppString* mix = il2cpp::il2cpp_string_new_from_cs("a1B2c3");
     Expect(il2cpp::il2cpp_char_count_digit(mix)==3,"count digit 3");
     Expect(il2cpp::il2cpp_char_count_letter(mix)==3,"count letter 3");
     Expect(il2cpp::il2cpp_char_count_letter(d)==0,"count letter 0");
     il2cpp::Il2CppString* ws = il2cpp::il2cpp_string_new_from_cs("a b\tc");
     Expect(il2cpp::il2cpp_char_count_whitespace(ws)==2,"count ws 2");
     il2cpp::Il2CppString* allws = il2cpp::il2cpp_string_new_from_cs(" \t\n");
     Expect(il2cpp::il2cpp_char_count_whitespace(allws)==3,"count ws 3");
     Expect(il2cpp::il2cpp_char_count_whitespace(d)==0,"count ws 0");
     Expect(il2cpp::il2cpp_char_count_digit(nullptr)==0,"count digit null 0");
     Expect(il2cpp::il2cpp_char_count_whitespace(nullptr)==0,"count ws null 0");

     Expect(il2cpp::il2cpp_char_is_digit_bulk(u'5'),"is digit 5 true");
     Expect(!il2cpp::il2cpp_char_is_digit_bulk(u'a'),"is digit a false");
     Expect(il2cpp::il2cpp_char_is_letter_bulk(u'Z'),"is letter Z true");
     Expect(!il2cpp::il2cpp_char_is_letter_bulk(u'0'),"is letter 0 false");

     // 34-len bulk
     il2cpp::Il2CppString* s32 = il2cpp::il2cpp_string_new_from_cs("0123456789abcdefgh0123456789ABCDEF");
     Expect(il2cpp::il2cpp_char_count_digit(s32)==20,"count digit 34 20");
     Expect(il2cpp::il2cpp_char_count_letter(s32)==14,"count letter 34 14");

     if(g_failures){ std::printf("%d char_bulk test(s) failed\n",g_failures); return 1; }
     std::printf("all char_bulk tests passed\n"); return 0;
 }
