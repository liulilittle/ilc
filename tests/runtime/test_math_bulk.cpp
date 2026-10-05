//------------------------------------------------------------------------------
 // test_math_bulk.cpp -- math_bulk bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "math_bulk.h"
 #include <cstdio>
 namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } }
 int main(){
     Expect(il2cpp::il2cpp_math_is_pow2_u32(1),"pow2 1 true");
     Expect(il2cpp::il2cpp_math_is_pow2_u32(2),"pow2 2 true");
     Expect(il2cpp::il2cpp_math_is_pow2_u32(1u<<31),"pow2 2^31 true");
     Expect(!il2cpp::il2cpp_math_is_pow2_u32(0),"pow2 0 false");
     Expect(!il2cpp::il2cpp_math_is_pow2_u32(3),"pow2 3 false");
     Expect(il2cpp::il2cpp_math_is_pow2_u64(1ull<<63),"pow2 64 true");
     Expect(!il2cpp::il2cpp_math_is_pow2_u64(0),"pow2 64 0 false");
     Expect(il2cpp::il2cpp_math_ctz_u32(1)==0,"ctz 1 0");
     Expect(il2cpp::il2cpp_math_ctz_u32(2)==1,"ctz 2 1");
     Expect(il2cpp::il2cpp_math_ctz_u32(8)==3,"ctz 8 3");
     Expect(il2cpp::il2cpp_math_ctz_u32(0)==32,"ctz 0 32");
     Expect(il2cpp::il2cpp_math_ctz_u64(1ull)==0,"ctz64 1 0");
     Expect(il2cpp::il2cpp_math_ctz_u64(0)==64,"ctz64 0 64");
     Expect(il2cpp::il2cpp_math_clz_u32(0)==32,"clz 0 32");
     Expect(il2cpp::il2cpp_math_clz_u32(1)==31,"clz 1 31");
     Expect(il2cpp::il2cpp_math_clz_u32(0x80000000u)==0,"clz top 0");
     Expect(il2cpp::il2cpp_math_clz_u64(0)==64,"clz64 0 64");
     Expect(il2cpp::il2cpp_math_popcount_u32(0)==0,"pop 0 0");
     Expect(il2cpp::il2cpp_math_popcount_u32(0xFFFFFFFFu)==32,"pop all 32");
     Expect(il2cpp::il2cpp_math_popcount_u32(0b1011)==3,"pop 1011 3");
     Expect(il2cpp::il2cpp_math_popcount_u64(0)==0,"pop64 0 0");
     Expect(il2cpp::il2cpp_math_popcount_u64(0xFFFFFFFFFFFFFFFFull)==64,"pop64 all 64");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u32(0)==1,"round 0 1");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u32(1)==1,"round 1 1");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u32(3)==4,"round 3 4");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u32(5)==8,"round 5 8");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u32(1<<30)==1<<30,"round pow2 stay");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u64(0)==1,"round64 0 1");
     Expect(il2cpp::il2cpp_math_round_up_pow2_u64(9)==16,"round64 9 16");
     uint32_t arr[16]={1,2,3,4,8,16,7,9,32,0,1,2,6,12,64,100};
     Expect(il2cpp::il2cpp_math_count_pow2_bulk_u32(arr,16)==9,"count bulk 9"); // 1,2,4,8,16,32,1,2,64 =9 (0 not pow2)
     Expect(il2cpp::il2cpp_math_count_pow2_bulk_u32(nullptr,0)==0,"count null 0");
     Expect(il2cpp::il2cpp_math_count_pow2_bulk_u32(arr,0)==0,"count 0 0");
     if(g_failures){ std::printf("%d math_bulk test(s) failed\n",g_failures); return 1; }
     std::printf("all math_bulk tests passed\n"); return 0;
 }
