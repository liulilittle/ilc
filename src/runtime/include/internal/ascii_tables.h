#pragma once
// ascii_tables.h -- ASCII fast tables, 256 entries each, GC-free.
#include <cstdint>
namespace il2cpp {
extern const uint8_t  kAsciiIsDigit[256];
extern const uint8_t  kAsciiIsAlpha[256];
extern const uint8_t  kAsciiIsAlphaNum[256];
extern const uint8_t  kAsciiIsSpace[256];
extern const char16_t kAsciiToUpper[256];
extern const char16_t kAsciiToLower[256];
extern const int8_t   kAsciiDigitValue[256]; // -1 if not digit else 0-9
} // namespace il2cpp
