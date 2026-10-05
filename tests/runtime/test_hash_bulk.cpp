#include "hash_bulk.h"
#include "il2cpp_string.h"
#include "il2cpp_gc.h"
#include <cstdio>
#include <cstring>
namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } void Report(){ if(g_failures) std::printf("%d hash_bulk test(s) failed\n",g_failures); else std::printf("all hash_bulk tests passed\n"); } }
static il2cpp::Il2CppString* NewStr(const char* s){ size_t n=strlen(s); il2cpp::Il2CppString* r=il2cpp::il2cpp_string_new(nullptr,(int32_t)n); for(size_t i=0;i<n;++i) r->chars[i]=(char16_t)(unsigned char)s[i]; return r; }
int main(){
    il2cpp::il2cpp_gc_init();
    // string ordinal
    { auto* s=NewStr("abc"); uint32_t h1=il2cpp::il2cpp_string_hash_ordinal(s); uint32_t h2=il2cpp::il2cpp_string_hash_ordinal(s); Expect(h1==h2,"hash deterministic"); Expect(h1!=2166136261u,"hash not seed"); }
    { auto* s=NewStr(""); Expect(il2cpp::il2cpp_string_hash_ordinal(s)==2166136261u,"empty seed"); }
    { Expect(il2cpp::il2cpp_string_hash_ordinal(nullptr)==2166136261u,"null seed"); }
    // ignore case vs ordinal
    { auto* a=NewStr("AbC"); auto* b=NewStr("abc"); Expect(il2cpp::il2cpp_string_hash_ordinal_ignore_case(a)==il2cpp::il2cpp_string_hash_ordinal_ignore_case(b),"ignore case equal"); Expect(il2cpp::il2cpp_string_hash_ordinal(a)!=il2cpp::il2cpp_string_hash_ordinal(b),"case sensitive diff"); }
    // long (>8) hashing
    { auto* s=NewStr("0123456789ABCDEF"); uint32_t h=il2cpp::il2cpp_string_hash_ordinal(s); Expect(h!=0,"long hash nonzero"); }
    // buffer hash
    { const char data[]="hello world"; uint32_t h=il2cpp::il2cpp_buffer_hash_fnv1a(data, sizeof(data)-1); Expect(h!=2166136261u,"buffer hash"); Expect(il2cpp::il2cpp_buffer_hash_fnv1a(nullptr,0)==2166136261u,"buffer null seed"); Expect(il2cpp::il2cpp_buffer_hash_fnv1a(data,0)==2166136261u,"buffer 0 len seed"); }
    { const char b1[]="abcd1234"; uint32_t h1=il2cpp::il2cpp_buffer_hash_fnv1a(b1,8); const char b2[]="abcd1235"; uint32_t h2=il2cpp::il2cpp_buffer_hash_fnv1a(b2,8); Expect(h1!=h2,"buffer diff"); }
    // array i4
    { int32_t arr[]={1,2,3,4,5,6,7,8,9}; uint32_t h=il2cpp::il2cpp_array_hash_i4_bulk(arr,9); Expect(h!=2166136261u,"array hash"); Expect(il2cpp::il2cpp_array_hash_i4_bulk(nullptr,0)==2166136261u,"array null"); int32_t arr2[]={1,2,3,4,5,6,7,8,10}; Expect(il2cpp::il2cpp_array_hash_i4_bulk(arr,9)!=il2cpp::il2cpp_array_hash_i4_bulk(arr2,9),"array diff"); }
    Report();
    return g_failures==0?0:1;
}
