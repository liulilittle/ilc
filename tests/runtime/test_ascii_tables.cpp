//------------------------------------------------------------------------------
 // test_ascii_tables.cpp -- ascii_tables 256-entry tables unit tests.
 //------------------------------------------------------------------------------
 #include "ascii_tables.h"
 #include <cstdio>
 namespace { int g_failures=0; void Expect(bool c,const char* w){ if(!c){ std::printf("FAIL: %s\n",w); ++g_failures; } } }
 int main(){
     // IsDigit
     for(int i=0;i<256;++i){
         bool exp = (i>='0' && i<='9');
         Expect((il2cpp::kAsciiIsDigit[i]!=0)==exp, "IsDigit table");
         if((il2cpp::kAsciiIsDigit[i]!=0)!=exp) break;
     }
     Expect(il2cpp::kAsciiIsDigit[static_cast<unsigned char>('0')]==1,"IsDigit 0 true");
     Expect(il2cpp::kAsciiIsDigit[static_cast<unsigned char>('9')]==1,"IsDigit 9 true");
     Expect(il2cpp::kAsciiIsDigit[static_cast<unsigned char>('a')]==0,"IsDigit a false");
     // IsAlpha
     Expect(il2cpp::kAsciiIsAlpha[static_cast<unsigned char>('A')]==1,"IsAlpha A true");
     Expect(il2cpp::kAsciiIsAlpha[static_cast<unsigned char>('z')]==1,"IsAlpha z true");
     Expect(il2cpp::kAsciiIsAlpha[static_cast<unsigned char>('0')]==0,"IsAlpha 0 false");
     // IsAlphaNum
     Expect(il2cpp::kAsciiIsAlphaNum[static_cast<unsigned char>('A')]==1,"IsAlphaNum A true");
     Expect(il2cpp::kAsciiIsAlphaNum[static_cast<unsigned char>('0')]==1,"IsAlphaNum 0 true");
     Expect(il2cpp::kAsciiIsAlphaNum[static_cast<unsigned char>('-')]==0,"IsAlphaNum - false");
     // IsSpace
     Expect(il2cpp::kAsciiIsSpace[static_cast<unsigned char>(' ')]==1,"IsSpace space true");
     Expect(il2cpp::kAsciiIsSpace[static_cast<unsigned char>('\t')]==1,"IsSpace tab true");
     Expect(il2cpp::kAsciiIsSpace[static_cast<unsigned char>('\n')]==1,"IsSpace nl true");
     Expect(il2cpp::kAsciiIsSpace[static_cast<unsigned char>('a')]==0,"IsSpace a false");
     // ToUpper / ToLower
     Expect(il2cpp::kAsciiToUpper[static_cast<unsigned char>('a')]=='A',"ToUpper a->A");
     Expect(il2cpp::kAsciiToUpper[static_cast<unsigned char>('z')]=='Z',"ToUpper z->Z");
     Expect(il2cpp::kAsciiToUpper[static_cast<unsigned char>('A')]=='A',"ToUpper A stay");
     Expect(il2cpp::kAsciiToUpper[static_cast<unsigned char>('0')]=='0',"ToUpper 0 stay");
     Expect(il2cpp::kAsciiToLower[static_cast<unsigned char>('A')]=='a',"ToLower A->a");
     Expect(il2cpp::kAsciiToLower[static_cast<unsigned char>('Z')]=='z',"ToLower Z->z");
     Expect(il2cpp::kAsciiToLower[static_cast<unsigned char>('a')]=='a',"ToLower a stay");
     // DigitValue
     Expect(il2cpp::kAsciiDigitValue[static_cast<unsigned char>('0')]==0,"DigitValue 0 0");
     Expect(il2cpp::kAsciiDigitValue[static_cast<unsigned char>('9')]==9,"DigitValue 9 9");
     Expect(il2cpp::kAsciiDigitValue[static_cast<unsigned char>('a')]==-1,"DigitValue a -1");
     Expect(il2cpp::kAsciiDigitValue[static_cast<unsigned char>(' ')]==-1,"DigitValue space -1");
     // bulk spot check all 256 via loop for ToUpper
     for(int i=0;i<256;++i){
         char16_t exp = (i>='a' && i<='z') ? (char16_t)(i-32) : (char16_t)i;
         if(il2cpp::kAsciiToUpper[i]!=exp){ Expect(false,"ToUpper bulk"); break; }
     }
     for(int i=0;i<256;++i){
         char16_t exp = (i>='A' && i<='Z') ? (char16_t)(i+32) : (char16_t)i;
         if(il2cpp::kAsciiToLower[i]!=exp){ Expect(false,"ToLower bulk"); break; }
     }
     if(g_failures){ std::printf("%d ascii_tables test(s) failed\n",g_failures); return 1; }
     std::printf("all ascii_tables tests passed\n"); return 0;
 }
