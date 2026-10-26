#pragma once
//------------------------------------------------------------------------------
// il2cpp_convertbase64.h -- System.Convert Base64 icall support.
//
// The transpiler maps Convert.ToBase64String(byte[]) / FromBase64String(string)
// to these runtime helpers -- the same icall pattern as convert.cpp.
//
// SEMANTIC BASELINE: .NET Framework 4.8 -- the standard RFC 4648 Base64
// alphabet ('A'-'Z','a'-'z','0'-'9','+','/') with '=' padding:
//   * ToBase64String: encodes in 3-byte quantums to 4 chars; the final
//     quantum is padded with '=' (1 or 2) when the input length is not a
//     multiple of 3. Null array -> ArgumentNullException.
//   * FromBase64String: ignores ASCII whitespace (space / tab / CR / LF)
//     anywhere in the input; '=' padding is only legal in the FINAL
//     quantum: a single '=' after exactly three valid chars, or '==' after
//     exactly two (whitespace may sit between the pads and trail after
//     them). net48 quirks (probe-verified 2026-08-04): "AB= " (single '='
//     after two chars + trailing whitespace) throws, while "TWE==" (three
//     valid chars + '==') decodes to ONE byte and "TWE" (unpadded three
//     chars) throws. A leftover single valid char (length % 4 == 1) and
//     unpadded leftovers of 2 or 3 chars throw; an illegal character ->
//     FormatException; null string -> ArgumentNullException.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "il2cpp_array.h"
#include "il2cpp_exception_ext.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// Convert.ToBase64String(byte[]): standard Base64 with '=' padding.
// Null array -> ArgumentNullException. Returns a managed string.
Il2CppString* il2cpp_convert_to_base64_string(Il2CppArray* bytes);

// Convert.FromBase64String(string): standard Base64 decode (see the
// semantics above). Null string -> ArgumentNullException; invalid input ->
// FormatException. Returns a byte[] (elemSize 1) managed array.
Il2CppArray* il2cpp_convert_from_base64_string(Il2CppString* text);

// BATCH 8: the remaining net48 Convert.ToBase64String / FromBase64CharArray
// overloads (all the offset/length/options forms). The offset/length pair is
// validated against the array length (ArgumentOutOfRangeException /
// ArgumentException); Base64FormattingOptions.InsertLineBreaks (1) inserts a
// CRLF every 76 output characters.
Il2CppString* il2cpp_convert_to_base64_string_range(Il2CppArray* bytes, int32_t offset, int32_t length);
Il2CppString* il2cpp_convert_to_base64_string_options(Il2CppArray* bytes, int32_t options);
Il2CppString* il2cpp_convert_to_base64_string_range_options(Il2CppArray* bytes, int32_t offset, int32_t length, int32_t options);
Il2CppArray* il2cpp_convert_from_base64_char_array(Il2CppArray* chars, int32_t offset, int32_t length);

// Convert.ToBase64CharArray(byte[], int, int, char[], int) and the
// Base64FormattingOptions overload (convert.cs:2193/2203): encode the byte
// range into outChars at offsetOut and return the number of chars written.
// Both arrays are validated in net48 order (inArray/outArray null, negative
// length/offsetIn/offsetOut, invalid options, offsetIn past the end, then --
// after the empty-input early return -- offsetOut capacity).
int32_t il2cpp_convert_to_base64_char_array(Il2CppArray* bytes, int32_t offsetIn, int32_t length,
                                            Il2CppArray* outChars, int32_t offsetOut);
int32_t il2cpp_convert_to_base64_char_array_options(Il2CppArray* bytes, int32_t offsetIn, int32_t length,
                                                    Il2CppArray* outChars, int32_t offsetOut, int32_t options);

}} // namespace il2cpp::runtime
