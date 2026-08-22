#define NOMINMAX
#include <windows.h>
#include <richedit.h>
#include <cstdio>
#include <cstddef>

int main()
{
    printf("sizeof(CHARFORMAT2W)  = %zu\n", sizeof(CHARFORMAT2W));
    printf("offsetof(cbSize)      = %zu\n", offsetof(CHARFORMAT2W, cbSize));
    printf("offsetof(dwMask)      = %zu\n", offsetof(CHARFORMAT2W, dwMask));
    printf("offsetof(dwEffects)   = %zu\n", offsetof(CHARFORMAT2W, dwEffects));
    printf("offsetof(yHeight)     = %zu\n", offsetof(CHARFORMAT2W, yHeight));
    printf("offsetof(yOffset)     = %zu\n", offsetof(CHARFORMAT2W, yOffset));
    printf("offsetof(crTextColor) = %zu\n", offsetof(CHARFORMAT2W, crTextColor));
    printf("offsetof(bCharSet)    = %zu\n", offsetof(CHARFORMAT2W, bCharSet));
    printf("offsetof(bPitchAndFamily) = %zu\n", offsetof(CHARFORMAT2W, bPitchAndFamily));
    printf("offsetof(szFaceName)  = %zu\n", offsetof(CHARFORMAT2W, szFaceName));
    printf("offsetof(wWeight)     = %zu\n", offsetof(CHARFORMAT2W, wWeight));
    printf("offsetof(sSpacing)    = %zu\n", offsetof(CHARFORMAT2W, sSpacing));
    printf("offsetof(crBackColor) = %zu\n", offsetof(CHARFORMAT2W, crBackColor));
    printf("offsetof(lcid)        = %zu\n", offsetof(CHARFORMAT2W, lcid));
    printf("offsetof(dwReserved)  = %zu\n", offsetof(CHARFORMAT2W, dwReserved));
    printf("offsetof(sStyle)      = %zu\n", offsetof(CHARFORMAT2W, sStyle));
    printf("offsetof(wKerning)    = %zu\n", offsetof(CHARFORMAT2W, wKerning));

    printf("EM_SETEVENTMASK = 0x%X\n", EM_SETEVENTMASK);
    printf("EM_SETCHARFORMAT = 0x%X\n", EM_SETCHARFORMAT);
    printf("EM_GETCHARFORMAT = 0x%X\n", EM_GETCHARFORMAT);
    printf("EM_SETSEL = 0x%X\n", EM_SETSEL);
    printf("EM_REPLACESEL = 0x%X\n", EM_REPLACESEL);
    printf("ENM_NONE = 0x%X\n", ENM_NONE);
    printf("SCF_SELECTION = 0x%X\n", SCF_SELECTION);
    printf("CFM_COLOR = 0x%X\n", CFM_COLOR);
    printf("CFE_AUTOCOLOR = 0x%X\n", CFE_AUTOCOLOR);
    printf("ES_MULTILINE = 0x%X\n", ES_MULTILINE);
    printf("ES_AUTOVSCROLL = 0x%X\n", ES_AUTOVSCROLL);
    return 0;
}