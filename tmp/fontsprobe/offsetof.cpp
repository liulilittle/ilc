#define NOMINMAX
#include <windows.h>
#include <commdlg.h>
#include <cstdio>
#include <cstddef>

int main()
{
    printf("sizeof(LOGFONTW)    = %zu\n", sizeof(LOGFONTW));
    printf("offsetof(lfHeight)  = %zu\n", offsetof(LOGFONTW, lfHeight));
    printf("offsetof(lfWeight)  = %zu\n", offsetof(LOGFONTW, lfWeight));
    printf("offsetof(lfItalic)  = %zu\n", offsetof(LOGFONTW, lfItalic));
    printf("offsetof(lfCharSet) = %zu\n", offsetof(LOGFONTW, lfCharSet));
    printf("offsetof(lfFaceName)= %zu\n", offsetof(LOGFONTW, lfFaceName));
    printf("lfFaceName size     = %zu\n", sizeof(((LOGFONTW*)0)->lfFaceName));

    printf("sizeof(CHOOSEFONTW)  = %zu\n", sizeof(CHOOSEFONTW));
    printf("offsetof(lStructSize) = %zu\n", offsetof(CHOOSEFONTW, lStructSize));
    printf("offsetof(hwndOwner)   = %zu\n", offsetof(CHOOSEFONTW, hwndOwner));
    printf("offsetof(hDC)         = %zu\n", offsetof(CHOOSEFONTW, hDC));
    printf("offsetof(lpLogFont)   = %zu\n", offsetof(CHOOSEFONTW, lpLogFont));
    printf("offsetof(iPointSize)  = %zu\n", offsetof(CHOOSEFONTW, iPointSize));
    printf("offsetof(Flags)       = %zu\n", offsetof(CHOOSEFONTW, Flags));
    printf("offsetof(rgbColors)   = %zu\n", offsetof(CHOOSEFONTW, rgbColors));
    printf("offsetof(lCustData)   = %zu\n", offsetof(CHOOSEFONTW, lCustData));
    printf("offsetof(lpfnHook)    = %zu\n", offsetof(CHOOSEFONTW, lpfnHook));
    printf("offsetof(lpTemplateName) = %zu\n", offsetof(CHOOSEFONTW, lpTemplateName));

    printf("CF_INITTOLOGFONTSTRUCT = 0x%X\n", CF_INITTOLOGFONTSTRUCT);
    printf("FW_NORMAL = %d\n", FW_NORMAL);
    printf("DEFAULT_CHARSET = %d\n", DEFAULT_CHARSET);
    return 0;
}
