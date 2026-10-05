//------------------------------------------------------------------------------
 // test_array_find.cpp -- array_find bulk helpers unit tests.
 //------------------------------------------------------------------------------
 #include "il2cpp_array.h"
 #include <cstdio>
 namespace {
 int g_failures=0;
 void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } }
 }
 int main(){
     // null / empty
     Expect(il2cpp::il2cpp_array_index_of_i4(nullptr,42,0)==-1,"index null -1");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(nullptr,42,0)==-1,"last null -1");
     Expect(!il2cpp::il2cpp_array_contains_i4(nullptr,42),"contains null false");
     Expect(il2cpp::il2cpp_array_count_i4(nullptr,42,0,10)==0,"count null 0");
     Expect(!il2cpp::il2cpp_array_try_bulk_copy_i4(nullptr,0,nullptr,0,1),"bulk null false");
     il2cpp::Il2CppArray* empty = il2cpp::il2cpp_array_new(nullptr,4,false,0);
     Expect(il2cpp::il2cpp_array_index_of_i4(empty,1,0)==-1,"index empty -1");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(empty,1,0)==-1,"last empty -1");
     Expect(!il2cpp::il2cpp_array_contains_i4(empty,1),"contains empty false");
     Expect(il2cpp::il2cpp_array_count_i4(empty,1,0,1)==0,"count empty 0");

     // basic [1,2,3,2,1]
     il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr,4,false,5);
     int32_t* p = reinterpret_cast<int32_t*>(a->data);
     p[0]=1; p[1]=2; p[2]=3; p[3]=2; p[4]=1;
     Expect(il2cpp::il2cpp_array_index_of_i4(a,1,0)==0,"index 1 0");
     Expect(il2cpp::il2cpp_array_index_of_i4(a,1,1)==4,"index 1 4");
     Expect(il2cpp::il2cpp_array_index_of_i4(a,2,0)==1,"index 2 1");
     Expect(il2cpp::il2cpp_array_index_of_i4(a,9,0)==-1,"index 9 -1");
     Expect(il2cpp::il2cpp_array_index_of_i4(a,2,-5)==1,"index neg start 1");
     Expect(il2cpp::il2cpp_array_index_of_i4(a,1,10)==-1,"index beyond -1");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a,1,4)==4,"last 1 4");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a,1,3)==0,"last 1 0");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a,2,4)==3,"last 2 3");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a,9,4)==-1,"last 9 -1");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a,2,10)==3,"last beyond 3");
     Expect(il2cpp::il2cpp_array_contains_i4(a,3),"contains 3");
     Expect(!il2cpp::il2cpp_array_contains_i4(a,9),"not contains 9");
     Expect(il2cpp::il2cpp_array_count_i4(a,1,0,5)==2,"count 1 2");
     Expect(il2cpp::il2cpp_array_count_i4(a,2,0,5)==2,"count 2 2");
     Expect(il2cpp::il2cpp_array_count_i4(a,1,1,2)==0,"count slice 0");
     Expect(il2cpp::il2cpp_array_count_i4(a,2,1,2)==1,"count slice 1");
     Expect(il2cpp::il2cpp_array_count_i4(a,1,10,5)==0,"count beyond 0");

     // 16-len bulk
     il2cpp::Il2CppArray* a16 = il2cpp::il2cpp_array_new(nullptr,4,false,16);
     int32_t* p16 = reinterpret_cast<int32_t*>(a16->data);
     for(int i=0;i<16;++i) p16[i]=i;
     Expect(il2cpp::il2cpp_array_index_of_i4(a16,8,0)==8,"index 8 bulk 8");
     Expect(il2cpp::il2cpp_array_index_of_i4(a16,15,0)==15,"index 15");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a16,0,15)==0,"last 0");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a16,15,15)==15,"last 15");
     Expect(il2cpp::il2cpp_array_count_i4(a16,5,0,16)==1,"count 5 1");

     // 32-len bulk
     il2cpp::Il2CppArray* a32 = il2cpp::il2cpp_array_new(nullptr,4,false,32);
     int32_t* p32 = reinterpret_cast<int32_t*>(a32->data);
     for(int i=0;i<32;++i) p32[i]=i%8;
     Expect(il2cpp::il2cpp_array_index_of_i4(a32,7,0)==7,"index 7 7");
     Expect(il2cpp::il2cpp_array_index_of_i4(a32,7,8)==15,"index 7 15");
     Expect(il2cpp::il2cpp_array_last_index_of_i4(a32,0,31)==24,"last 0 24");
     Expect(il2cpp::il2cpp_array_count_i4(a32,3,0,32)==4,"count 3 4");

     // bulk copy
     il2cpp::Il2CppArray* src = il2cpp::il2cpp_array_new(nullptr,4,false,4);
     il2cpp::Il2CppArray* dst = il2cpp::il2cpp_array_new(nullptr,4,false,4);
     int32_t* sp = reinterpret_cast<int32_t*>(src->data);
     int32_t* dp = reinterpret_cast<int32_t*>(dst->data);
     sp[0]=10; sp[1]=20; sp[2]=30; sp[3]=40;
     dp[0]=0; dp[1]=0; dp[2]=0; dp[3]=0;
     Expect(il2cpp::il2cpp_array_try_bulk_copy_i4(dst,1,src,1,2),"bulk copy true");
     Expect(dp[1]==20 && dp[2]==30,"bulk copy values");
     Expect(dp[0]==0 && dp[3]==0,"bulk copy untouched");
     // overlapping copy within same array (memmove)
     il2cpp::Il2CppArray* ov = il2cpp::il2cpp_array_new(nullptr,4,false,5);
     int32_t* op = reinterpret_cast<int32_t*>(ov->data);
     for(int i=0;i<5;++i) op[i]=i;
     Expect(il2cpp::il2cpp_array_try_bulk_copy_i4(ov,1,ov,0,3),"overlap copy true");
     Expect(op[0]==0 && op[1]==0 && op[2]==1 && op[3]==2,"overlap values");
     Expect(!il2cpp::il2cpp_array_try_bulk_copy_i4(dst,0,src,0,10),"bulk out of range false");
     // elemSize mismatch
     il2cpp::Il2CppArray* bad = il2cpp::il2cpp_array_new(nullptr,8,false,2);
     Expect(il2cpp::il2cpp_array_index_of_i4(bad,1,0)==-1,"elemSize mismatch -1");
     Expect(!il2cpp::il2cpp_array_try_bulk_copy_i4(bad,0,src,0,1),"elemSize mismatch copy false");

     if(g_failures){ std::printf("%d array_find test(s) failed\n",g_failures); return 1; }
     std::printf("all array_find tests passed\n"); return 0;
 }
