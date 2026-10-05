//------------------------------------------------------------------------------
 // test_string_encode.cpp -- string_encode bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "string_encode.h"
 #include <cstdio>
 #include <cstring>
 namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } }
 int main(){
     Expect(il2cpp::il2cpp_utf8_is_ascii(nullptr,0),"utf8 ascii null true");
     Expect(il2cpp::il2cpp_utf8_is_ascii("",0),"utf8 ascii empty true");
     Expect(il2cpp::il2cpp_utf8_is_ascii("hello",5),"utf8 ascii hello true");
     Expect(!il2cpp::il2cpp_utf8_is_ascii("\xC2\xA9",2),"utf8 ascii non true false");
     // 16 ascii
     Expect(il2cpp::il2cpp_utf8_is_ascii("abcdefghABCDEFGH",16),"utf8 ascii 16 true");
     Expect(!il2cpp::il2cpp_utf8_is_ascii("abcdefgh\xC2\xA9",10),"utf8 ascii 16 false");
     Expect(il2cpp::il2cpp_utf16_is_ascii_raw(nullptr,0),"utf16 ascii null true");
     char16_t e16[1]={0}; Expect(il2cpp::il2cpp_utf16_is_ascii_raw(e16,0),"utf16 ascii empty true");
     char16_t a16[16]; for(int i=0;i<16;++i) a16[i]=u'a'; Expect(il2cpp::il2cpp_utf16_is_ascii_raw(a16,16),"utf16 ascii 16 true");
     a16[8]=0x80; Expect(!il2cpp::il2cpp_utf16_is_ascii_raw(a16,16),"utf16 ascii 16 false");
     // utf8->utf16 length
     Expect(il2cpp::il2cpp_utf8_to_utf16_length(nullptr,0)==0,"utf8 len null 0");
     Expect(il2cpp::il2cpp_utf8_to_utf16_length("abc",3)==3,"utf8 len ascii 3");
     Expect(il2cpp::il2cpp_utf8_to_utf16_length("\xC2\xA9",2)==1,"utf8 len 2->1");
     Expect(il2cpp::il2cpp_utf8_to_utf16_length("\xE2\x98\x83",3)==1,"utf8 len 3->1");
     Expect(il2cpp::il2cpp_utf8_to_utf16_length("\xF0\x9F\x98\x80",4)==2,"utf8 len 4->2 surrogate");
     // utf16->utf8 length
     Expect(il2cpp::il2cpp_utf16_to_utf8_length(nullptr,0)==0,"utf16 len null 0");
     char16_t u3[1]={u'a'}; Expect(il2cpp::il2cpp_utf16_to_utf8_length(u3,1)==1,"utf16 len ascii 1");
     char16_t u2b[1]={0xA9}; Expect(il2cpp::il2cpp_utf16_to_utf8_length(u2b,1)==2,"utf16 len 2bytes 2");
     char16_t u3b[1]={0x2603}; Expect(il2cpp::il2cpp_utf16_to_utf8_length(u3b,1)==3,"utf16 len 3bytes 3");
     char16_t pair[2]={0xD83D,0xDE00}; Expect(il2cpp::il2cpp_utf16_to_utf8_length(pair,2)==4,"utf16 len pair 4");
     // decode one
     char32_t out=0; Expect(il2cpp::il2cpp_utf8_decode_one("A",1,&out)==1 && out==0x41,"decode A");
     Expect(il2cpp::il2cpp_utf8_decode_one("\xC2\xA9",2,&out)==2 && out==0xA9,"decode 2");
     Expect(il2cpp::il2cpp_utf8_decode_one("\xE2\x98\x83",3,&out)==3 && out==0x2603,"decode 3");
     Expect(il2cpp::il2cpp_utf8_decode_one("\xF0\x9F\x98\x80",4,&out)==4 && out==0x1F600,"decode 4");
     Expect(il2cpp::il2cpp_utf8_decode_one("\xFF",1,&out)==0,"decode invalid 0");
     Expect(il2cpp::il2cpp_utf8_decode_one("\xC2",1,&out)==0,"decode truncated 0");
     // validate
     Expect(il2cpp::il2cpp_utf8_validate(nullptr,0)==0,"validate null 0");
     Expect(il2cpp::il2cpp_utf8_validate("",0)==1,"validate empty 1");
     Expect(il2cpp::il2cpp_utf8_validate("hello",5)==1,"validate ascii 1");
     Expect(il2cpp::il2cpp_utf8_validate("\xC2\xA9",2)==1,"validate 2 1");
     Expect(il2cpp::il2cpp_utf8_validate("\xFF",1)==0,"validate invalid 0");
     Expect(il2cpp::il2cpp_utf8_validate("\xC2",1)==0,"validate truncated 0");
     if(g_failures){ std::printf("%d string_encode test(s) failed\n",g_failures); return 1; }
     std::printf("all string_encode tests passed\n"); return 0;
 }
