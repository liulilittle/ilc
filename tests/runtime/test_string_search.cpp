//------------------------------------------------------------------------------
 // test_string_search.cpp -- string_search bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "il2cpp_string.h"
 #include <cstdio>
 namespace {
 int g_failures=0;
 void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } }
 }
 int main(){
     // null / empty
     Expect(il2cpp::il2cpp_string_index_of_char(nullptr,u'a',0)==-1,"index null -1");
     Expect(il2cpp::il2cpp_string_last_index_of_char(nullptr,u'a',0)==-1,"last null -1");
     Expect(!il2cpp::il2cpp_string_contains_char(nullptr,u'a'),"contains null false");
     Expect(il2cpp::il2cpp_string_count_char(nullptr,u'a',0,10)==0,"count null 0");
     Expect(il2cpp::il2cpp_string_starts_with(nullptr,nullptr),"starts null+null true");
     Expect(il2cpp::il2cpp_string_ends_with(nullptr,nullptr),"ends null+null true");
     // empty string
     il2cpp::Il2CppString* e = il2cpp::il2cpp_string_new(nullptr,0);
     Expect(il2cpp::il2cpp_string_index_of_char(e,u'a',0)==-1,"index empty -1");
     Expect(il2cpp::il2cpp_string_last_index_of_char(e,u'a',0)==-1,"last empty -1");
     Expect(!il2cpp::il2cpp_string_contains_char(e,u'a'),"contains empty false");
     Expect(il2cpp::il2cpp_string_count_char(e,u'a',0,1)==0,"count empty 0");
     il2cpp::Il2CppString* p0 = il2cpp::il2cpp_string_new(nullptr,0);
     Expect(il2cpp::il2cpp_string_starts_with(e,p0),"starts empty true");
     Expect(il2cpp::il2cpp_string_ends_with(e,p0),"ends empty true");

     // basic IndexOf / Last / Contains / Count
     il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new_from_cs("ababa");
     Expect(il2cpp::il2cpp_string_index_of_char(s,u'a',0)==0,"index a 0");
     Expect(il2cpp::il2cpp_string_index_of_char(s,u'a',1)==2,"index a 2");
     Expect(il2cpp::il2cpp_string_index_of_char(s,u'a',3)==4,"index a 4");
     Expect(il2cpp::il2cpp_string_index_of_char(s,u'x',0)==-1,"index x -1");
     Expect(il2cpp::il2cpp_string_index_of_char(s,u'b',-5)==1,"index b neg start 1");
     Expect(il2cpp::il2cpp_string_index_of_char(s,u'a',10)==-1,"index beyond -1");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s,u'a',4)==4,"last a 4");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s,u'a',3)==2,"last a 2");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s,u'a',0)==0,"last a 0");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s,u'x',4)==-1,"last x -1");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s,u'b',10)==3,"last b beyond 3");
     Expect(il2cpp::il2cpp_string_contains_char(s,u'b'),"contains b");
     Expect(!il2cpp::il2cpp_string_contains_char(s,u'z'),"not contains z");
     Expect(il2cpp::il2cpp_string_count_char(s,u'a',0,5)==3,"count a 3");
     Expect(il2cpp::il2cpp_string_count_char(s,u'b',0,5)==2,"count b 2");
     Expect(il2cpp::il2cpp_string_count_char(s,u'a',1,2)==1,"count slice 1");
     Expect(il2cpp::il2cpp_string_count_char(s,u'a',10,5)==0,"count beyond 0");

     // 16-char to exercise 8x bulk twice
     il2cpp::Il2CppString* s16 = il2cpp::il2cpp_string_new_from_cs("abcdefghABCDEFGH");
     Expect(il2cpp::il2cpp_string_index_of_char(s16,u'A',0)==8,"index A 8 bulk");
     Expect(il2cpp::il2cpp_string_index_of_char(s16,u'H',0)==15,"index H 15");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s16,u'a',15)==0,"last a 0");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s16,u'H',15)==15,"last H 15");
     Expect(il2cpp::il2cpp_string_count_char(s16,u'a',0,16)==1,"count a 16 1");
     Expect(il2cpp::il2cpp_string_count_char(s16,u'A',0,16)==1,"count A 1");

     // 32-char bulk
     il2cpp::Il2CppString* s32 = il2cpp::il2cpp_string_new_from_cs("abcdefghijklmnopqrstuvwxyz012345");
     Expect(il2cpp::il2cpp_string_index_of_char(s32,u'z',0)==25,"index z 25");
     Expect(il2cpp::il2cpp_string_index_of_char(s32,u'5',0)==31,"index 5 31");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s32,u'a',31)==0,"last a 0 32");
     Expect(il2cpp::il2cpp_string_last_index_of_char(s32,u'5',31)==31,"last 5 31");

     // StartsWith / EndsWith
     il2cpp::Il2CppString* pre = il2cpp::il2cpp_string_new_from_cs("ab");
     il2cpp::Il2CppString* suf = il2cpp::il2cpp_string_new_from_cs("ba");
     Expect(il2cpp::il2cpp_string_starts_with(s,pre),"starts ab");
     Expect(!il2cpp::il2cpp_string_starts_with(s,suf),"not starts ba");
     Expect(il2cpp::il2cpp_string_ends_with(s,suf),"ends ba");
     Expect(!il2cpp::il2cpp_string_ends_with(s,pre),"not ends ab");
     il2cpp::Il2CppString* longPre = il2cpp::il2cpp_string_new_from_cs("ababaX");
     Expect(!il2cpp::il2cpp_string_starts_with(s,longPre),"starts long false");
     Expect(!il2cpp::il2cpp_string_ends_with(s,longPre),"ends long false");

     // IndexOfAny bulk bitmap
     char16_t any1[2] = {u'x', u'b'};
     Expect(il2cpp::il2cpp_string_index_of_any_bulk(s,any1,2,0)==1,"any b 1");
     char16_t any2[3] = {u'z',u'y',u'x'};
     Expect(il2cpp::il2cpp_string_index_of_any_bulk(s,any2,3,0)==-1,"any miss -1");
     char16_t any3[1] = {u'a'};
     Expect(il2cpp::il2cpp_string_index_of_any_bulk(s,any3,1,2)==2,"any a 2");
     Expect(il2cpp::il2cpp_string_index_of_any_bulk(nullptr,any1,2,0)==-1,"any null -1");
     Expect(il2cpp::il2cpp_string_index_of_any_bulk(s,nullptr,0,0)==-1,"any null ptr -1");

     if(g_failures){ std::printf("%d string_search test(s) failed\n",g_failures); return 1; }
     std::printf("all string_search tests passed\n"); return 0;
 }
