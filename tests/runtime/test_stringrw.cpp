//------------------------------------------------------------------------------
// test_stringrw.cpp -- System.IO.StringReader / StringWriter runtime unit
// tests (see stringrw.h for the icall contract under test).
//
// Coverage (normal + edge + error paths):
//   1. read : character-by-character consumption, stable EOF (-1), astral
//      characters read as two surrogate units.
//   2. peek : next unit without consuming; EOF boundary.
//   3. read_line : CR / LF / CRLF termination (net48 -- a CRLF pair is one
//      terminator, a lone CR terminates the line), empty lines, EOF.
//      EOF returns nullptr.
//   4. read_to_end : remainder from the current cursor; empty result at
//      end-of-string (empty string, not nullptr).
//   5. write : append + get_string round-trip, repeated get_string, null
//      text no-op, non-ASCII content (UTF-16 direct, no transcoding).
//   6. write_line : CRLF appended; null text writes just the CRLF.
//   7. Buffer growth : many small writes into one large managed string.
//   8. Error paths : null handles on every function; null input text.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "stringrw.h"

#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {

static_assert(sizeof(void*) == 8, "stringrw test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Managed string <-> ASCII comparison helper.
bool StrEquals(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// Managed string <-> explicit UTF-16 unit comparison helper (non-ASCII
// round-trips: the written and read strings must match unit for unit).
bool StrEquals16(il2cpp::Il2CppObject* s, const char16_t* units, int32_t length)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    if (str->length != length)
    {
        return false;
    }
    for (int32_t i = 0; i < length; ++i)
    {
        if (str->chars[i] != units[i])
        {
            return false;
        }
    }
    return true;
}

// Build a managed string from an ASCII literal (test input helper).
il2cpp::Il2CppObject* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// Build a managed string from a UTF-16 literal (u"..."), excluding the
// implicit trailing NUL unit.
template <size_t N>
il2cpp::Il2CppObject* Cs16(const char16_t (&units)[N])
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(units, static_cast<int32_t>(N - 1)));
}

// Reader handles hold the wrapped managed string by reference (no copy), so
// the fixtures keep every input string alive for the whole main() run --
// the GC-reachability contract in stringrw.h.
std::vector<il2cpp::Il2CppObject*> g_keepalive;

il2cpp::Il2CppObject* Keep(il2cpp::Il2CppObject* s)
{
    g_keepalive.push_back(s);
    return s;
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    // --- 1. read: character-by-character consumption + stable EOF ----------
    il2cpp_stringreader_t r = il2cpp_stringreader_create(Keep(Cs("abc")));
    Expect(r != nullptr, "StringReader create succeeds");
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'a'),
           "read returns the first unit");
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'b'),
           "read returns the second unit");
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'c'),
           "read returns the third unit");
    Expect(il2cpp_stringreader_read(r) == -1, "read at end-of-string returns -1");
    Expect(il2cpp_stringreader_read(r) == -1, "read stays -1 after end-of-string");
    Expect(il2cpp_stringreader_close(r) == 0, "StringReader close returns 0");

    // Astral character (U+1F600): read as two surrogate units, .NET style.
    r = il2cpp_stringreader_create(Keep(Cs16(u"\U0001F600")));
    Expect(il2cpp_stringreader_read(r) == 0xD83D, "astral read returns the high surrogate");
    Expect(il2cpp_stringreader_read(r) == 0xDE00, "astral read returns the low surrogate");
    Expect(il2cpp_stringreader_read(r) == -1, "astral read then EOF");
    Expect(il2cpp_stringreader_close(r) == 0, "astral reader closes");

    // --- 2. peek: next unit without consuming + EOF boundary ---------------
    r = il2cpp_stringreader_create(Keep(Cs("ab")));
    Expect(il2cpp_stringreader_peek(r) == static_cast<int32_t>(u'a'),
           "peek returns the next unit");
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'a'),
           "peek does not consume the unit");
    Expect(il2cpp_stringreader_peek(r) == static_cast<int32_t>(u'b'),
           "peek returns the next unit after one read");
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'b'),
           "read consumes the peeked unit");
    Expect(il2cpp_stringreader_peek(r) == -1, "peek at end-of-string returns -1");
    Expect(il2cpp_stringreader_close(r) == 0, "peek reader closes");

    // --- 3. read_line: LF / CRLF / empty lines / lone CR / EOF --------------
    r = il2cpp_stringreader_create(Keep(Cs16(u"one\ntwo\r\nthree")));
    Il2CppObject* line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, "one"), "read_line returns the first line (LF)");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, "two"), "read_line strips CRLF");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, "three"), "read_line returns the last line");
    Expect(il2cpp_stringreader_read_line(r) == nullptr,
           "read_line at end-of-string returns null");
    Expect(il2cpp_stringreader_close(r) == 0, "lines reader closes");

    r = il2cpp_stringreader_create(Keep(Cs16(u"a\n\nb")));
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, "a"), "read_line with empty lines: first");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, ""), "empty line returns \"\" (not null)");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, "b"), "read_line with empty lines: last");
    Expect(il2cpp_stringreader_close(r) == 0, "empty-line reader closes");

    // net48: CR, LF and CRLF are ALL line terminators; a CRLF pair is
    // consumed as ONE terminator (corert StringReader.cs:182-189). A lone CR
    // terminates the line -- it is NOT content.
    r = il2cpp_stringreader_create(Keep(Cs16(u"a\rb\n")));
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals16(line, u"a", 1),
           "a lone CR terminates the line (net48)");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals16(line, u"b", 1),
           "read resumes after a lone-CR-terminated line");
    Expect(il2cpp_stringreader_read_line(r) == nullptr,
           "read_line after the last line returns null");
    Expect(il2cpp_stringreader_close(r) == 0, "lone-CR reader closes");

    // CRLF consumed as one terminator: "x\r\ny" is two lines, x and y.
    r = il2cpp_stringreader_create(Keep(Cs16(u"x\r\ny")));
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals16(line, u"x", 1),
           "CRLF is consumed as one terminator (first line)");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals16(line, u"y", 1),
           "CRLF is consumed as one terminator (second line)");
    Expect(il2cpp_stringreader_close(r) == 0, "CRLF-pair reader closes");

    // A lone CR at end-of-string terminates the final line.
    r = il2cpp_stringreader_create(Keep(Cs16(u"z\r")));
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals16(line, u"z", 1),
           "a trailing lone CR terminates the final line");
    Expect(il2cpp_stringreader_read_line(r) == nullptr,
           "read_line after a trailing lone CR returns null");
    Expect(il2cpp_stringreader_close(r) == 0, "trailing-CR reader closes");

    // --- 4. read_to_end: remainder from the cursor + empty result ----------
    r = il2cpp_stringreader_create(Keep(Cs("hello")));
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'h'),
           "read_to_end fixture: consume the first unit");
    Expect(il2cpp_stringreader_read(r) == static_cast<int32_t>(u'e'),
           "read_to_end fixture: consume the second unit");
    Il2CppObject* rest = il2cpp_stringreader_read_to_end(r);
    Expect(rest != nullptr && StrEquals(rest, "llo"), "read_to_end returns the remainder");
    rest = il2cpp_stringreader_read_to_end(r);
    Expect(rest != nullptr && StrEquals(rest, ""),
           "read_to_end at end-of-string returns \"\" (not null)");
    Expect(il2cpp_stringreader_read(r) == -1, "read_to_end consumed the cursor");
    Expect(il2cpp_stringreader_close(r) == 0, "read_to_end reader closes");

    r = il2cpp_stringreader_create(Keep(Cs("")));
    Expect(r != nullptr, "StringReader over an empty string creates");
    Expect(il2cpp_stringreader_read(r) == -1, "empty string read returns -1");
    Expect(il2cpp_stringreader_peek(r) == -1, "empty string peek returns -1");
    Expect(il2cpp_stringreader_read_line(r) == nullptr, "empty string read_line returns null");
    rest = il2cpp_stringreader_read_to_end(r);
    Expect(rest != nullptr && StrEquals(rest, ""), "empty string read_to_end returns \"\"");
    Expect(il2cpp_stringreader_close(r) == 0, "empty reader closes");

    // --- 5. write: append + get_string round-trip ---------------------------
    il2cpp_stringwriter_t w = il2cpp_stringwriter_create();
    Expect(w != nullptr, "StringWriter create succeeds");
    Expect(il2cpp_stringwriter_write(w, Cs("a")) == 0, "write(\"a\") returns 0");
    Expect(il2cpp_stringwriter_write(w, Cs("bc")) == 0, "write(\"bc\") returns 0");
    Il2CppObject* out = il2cpp_stringwriter_get_string(w);
    Expect(out != nullptr && StrEquals(out, "abc"), "get_string joins the writes");
    out = il2cpp_stringwriter_get_string(w);
    Expect(out != nullptr && StrEquals(out, "abc"),
           "get_string is repeatable and does not consume the buffer");
    Expect(il2cpp_stringwriter_write(w, Cs("d")) == 0, "write after get_string succeeds");
    out = il2cpp_stringwriter_get_string(w);
    Expect(out != nullptr && StrEquals(out, "abcd"), "buffer keeps accumulating after get_string");

    // Non-ASCII: UTF-16 direct, no transcoding involved.
    const char16_t accented[] = { u'h', 0x00E9, u'l', u'l', u'o' };
    Expect(il2cpp_stringwriter_write(w, reinterpret_cast<Il2CppObject*>(
              il2cpp_string_new(accented, 5))) == 0, "write accepts non-ASCII content");
    out = il2cpp_stringwriter_get_string(w);
    const char16_t expected1[] = { u'a', u'b', u'c', u'd', u'h', 0x00E9, u'l', u'l', u'o' };
    Expect(out != nullptr && StrEquals16(out, expected1, 9),
           "non-ASCII content round-trips unit for unit");
    Expect(il2cpp_stringwriter_close(w) == 0, "StringWriter close returns 0");

    // --- 6. write_line: CRLF appended; null text writes just the CRLF ------
    w = il2cpp_stringwriter_create();
    Expect(il2cpp_stringwriter_write_line(w, Cs("x")) == 0, "write_line(\"x\") returns 0");
    Expect(il2cpp_stringwriter_write(w, Cs("y")) == 0, "write(\"y\") after write_line returns 0");
    out = il2cpp_stringwriter_get_string(w);
    Expect(out != nullptr && StrEquals(out, "x\r\ny"), "write_line appends CRLF");

    Expect(il2cpp_stringwriter_write(w, nullptr) == 0, "write(null) is a no-op success");
    Expect(il2cpp_stringwriter_write_line(w, nullptr) == 0,
           "write_line(null) returns 0");
    out = il2cpp_stringwriter_get_string(w);
    Expect(out != nullptr && StrEquals(out, "x\r\ny\r\n"),
           "write_line(null) writes just the CRLF");
    Expect(il2cpp_stringwriter_close(w) == 0, "write_line writer closes");

    // Writer -> reader round-trip (the managed string produced by the writer
    // feeds a fresh reader).
    w = il2cpp_stringwriter_create();
    Expect(il2cpp_stringwriter_write_line(w, reinterpret_cast<Il2CppObject*>(
              il2cpp_string_new(accented, 5))) == 0, "round-trip fixture: write_line");
    Expect(il2cpp_stringwriter_write(w, Cs("z")) == 0, "round-trip fixture: write");
    Il2CppObject* produced = il2cpp_stringwriter_get_string(w);
    r = il2cpp_stringreader_create(produced);
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals16(line, accented, 5),
           "writer -> reader line round-trips non-ASCII unit for unit");
    line = il2cpp_stringreader_read_line(r);
    Expect(line != nullptr && StrEquals(line, "z"), "writer -> reader second line");
    Expect(il2cpp_stringreader_read_line(r) == nullptr,
           "writer -> reader end-of-string");
    Expect(il2cpp_stringreader_close(r) == 0, "round-trip reader closes");
    Expect(il2cpp_stringwriter_close(w) == 0, "round-trip writer closes");

    // --- 7. Buffer growth: many small writes into one large string ---------
    w = il2cpp_stringwriter_create();
    for (int32_t i = 0; i < 1000; ++i)
    {
        Expect(il2cpp_stringwriter_write(w, Cs("ab")) == 0, "growth write succeeds");
    }
    out = il2cpp_stringwriter_get_string(w);
    Expect(out != nullptr && StrEquals16(out, u"ab", 2) == false,
           "growth fixture: content is longer than one write");
    Expect(out != nullptr && reinterpret_cast<Il2CppString*>(out)->length == 2000,
           "1000 small writes accumulate to 2000 UTF-16 units");
    Expect(out != nullptr && reinterpret_cast<Il2CppString*>(out)->chars[1999] == u'b',
           "growth buffer last unit is correct");
    Expect(il2cpp_stringwriter_close(w) == 0, "growth writer closes");

    // --- 8. Error paths -----------------------------------------------------
    Expect(il2cpp_stringreader_create(nullptr) == nullptr,
           "reader create(null) returns null");
    Expect(il2cpp_stringreader_read(nullptr) == -1, "read(null) returns -1");
    Expect(il2cpp_stringreader_peek(nullptr) == -1, "peek(null) returns -1");
    Expect(il2cpp_stringreader_read_line(nullptr) == nullptr,
           "read_line(null) returns null");
    Expect(il2cpp_stringreader_read_to_end(nullptr) == nullptr,
           "read_to_end(null) returns null");
    Expect(il2cpp_stringreader_close(nullptr) == 1, "reader close(null) returns 1");
    Expect(il2cpp_stringwriter_write(nullptr, Cs("x")) == -1, "write(null writer) returns -1");
    Expect(il2cpp_stringwriter_write_line(nullptr, Cs("x")) == -1,
           "write_line(null writer) returns -1");
    Expect(il2cpp_stringwriter_get_string(nullptr) == nullptr,
           "get_string(null) returns null");
    Expect(il2cpp_stringwriter_close(nullptr) == 1, "writer close(null) returns 1");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d stringrw assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime stringrw tests passed\n");
    return 0;
}
