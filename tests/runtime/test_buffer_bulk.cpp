//------------------------------------------------------------------------------
 // test_buffer_bulk.cpp -- buffer_bulk bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "buffer_bulk.h"
 #include <cstdio>
 #include <cstring>
 namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } }
 int main(){
     // is_zero
     Expect(il2cpp::il2cpp_buffer_is_zero_bulk(nullptr,0),"is_zero null 0 true");
     Expect(il2cpp::il2cpp_buffer_is_zero_bulk(nullptr,10),"is_zero null true");
     uint8_t z8[8]={0}; Expect(il2cpp::il2cpp_buffer_is_zero_bulk(z8,8),"is_zero 8 true");
     uint8_t z16[16]={0}; z16[15]=1; Expect(!il2cpp::il2cpp_buffer_is_zero_bulk(z16,16),"is_zero 16 false tail");
     uint8_t z9[9]={0}; z9[0]=1; Expect(!il2cpp::il2cpp_buffer_is_zero_bulk(z9,9),"is_zero 9 false head");
     uint8_t zz[32]={0}; Expect(il2cpp::il2cpp_buffer_is_zero_bulk(zz,32),"is_zero 32 true");
     zz[7]=5; Expect(!il2cpp::il2cpp_buffer_is_zero_bulk(zz,32),"is_zero 32 false at 7");
     // memset
     uint8_t dst[16]; il2cpp::il2cpp_buffer_memset_bulk(dst,0xAB,16); for(int i=0;i<16;++i) Expect(dst[i]==0xAB,"memset 16 AB");
     uint8_t dst2[9]; il2cpp::il2cpp_buffer_memset_bulk(dst2,0x00,9); for(int i=0;i<9;++i) Expect(dst2[i]==0,"memset 9 zero");
     il2cpp::il2cpp_buffer_memset_bulk(nullptr,0xFF,0); // no crash
     // memcpy
     uint8_t src[16]; for(int i=0;i<16;++i) src[i]=(uint8_t)i;
     uint8_t cpy[16]={0}; il2cpp::il2cpp_buffer_memcpy_bulk(cpy,src,16); Expect(std::memcmp(cpy,src,16)==0,"memcpy 16");
     uint8_t s9[9]; for(int i=0;i<9;++i) s9[i]=(uint8_t)(i+10); uint8_t d9[9]={0}; il2cpp::il2cpp_buffer_memcpy_bulk(d9,s9,9); Expect(std::memcmp(d9,s9,9)==0,"memcpy 9");
     // overlapping memmove semantics
     uint8_t ov[8]={0,1,2,3,4,5,6,7}; il2cpp::il2cpp_buffer_memcpy_bulk(ov+2,ov,4); Expect(ov[2]==0 && ov[3]==1 && ov[4]==2 && ov[5]==3,"memcpy overlap");
     // memcmp
     uint8_t a[16]={0}, b[16]={0}; Expect(il2cpp::il2cpp_buffer_memcmp_bulk(a,b,16)==0,"memcmp equal 16");
     b[15]=1; Expect(il2cpp::il2cpp_buffer_memcmp_bulk(a,b,16)<0,"memcmp less tail");
     Expect(il2cpp::il2cpp_buffer_memcmp_bulk(b,a,16)>0,"memcmp greater tail");
     uint8_t a9[9]={0}, b9[9]={0}; b9[0]=1; Expect(il2cpp::il2cpp_buffer_memcmp_bulk(a9,b9,9)<0,"memcmp 9 head less");
     Expect(il2cpp::il2cpp_buffer_memcmp_bulk(a,a,16)==0,"memcmp same ptr 0");
     Expect(il2cpp::il2cpp_buffer_memcmp_bulk(nullptr,a,10)<0,"memcmp null less");
     Expect(il2cpp::il2cpp_buffer_memcmp_bulk(a,nullptr,10)>0,"memcmp null greater");
     Expect(il2cpp::il2cpp_buffer_memcmp_bulk(a,b,0)==0,"memcmp 0 true");
     // try_copy_checked
     uint8_t ddst[10]={0}, ssrc[10]; for(int i=0;i<10;++i) ssrc[i]=(uint8_t)i;
     Expect(il2cpp::il2cpp_buffer_try_copy_checked(ddst,10,ssrc,10,10),"try_copy 10 true");
     Expect(std::memcmp(ddst,ssrc,10)==0,"try_copy values");
     Expect(!il2cpp::il2cpp_buffer_try_copy_checked(ddst,5,ssrc,10,10),"try_copy dst too small false");
     Expect(!il2cpp::il2cpp_buffer_try_copy_checked(ddst,10,ssrc,5,10),"try_copy src too small false");
     Expect(!il2cpp::il2cpp_buffer_try_copy_checked(nullptr,10,ssrc,10,5),"try_copy null dst false");
     Expect(!il2cpp::il2cpp_buffer_try_copy_checked(ddst,10,nullptr,10,5),"try_copy null src false");
     Expect(il2cpp::il2cpp_buffer_try_copy_checked(ddst,10,ssrc,10,0),"try_copy 0 true");
     if(g_failures){ std::printf("%d buffer_bulk test(s) failed\n",g_failures); return 1; }
     std::printf("all buffer_bulk tests passed\n"); return 0;
 }
