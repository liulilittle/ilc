//------------------------------------------------------------------------------
// xml_parse.cpp -- T1 CONTRACT-FIRST implementation (see xml_ast.h).
//
// Recursive-descent parser for the basic-domain grammar:
//
//   document  := BOM? S* (decl | comment | S*)* element S* comment* S*
//   element   := '<' name (S* attr)* (S* '/>' | S* '>' content* '</' name S* '>')
//   attr      := name S* '=' S* ('"' value '"' | "'" value "'")
//   content   := text-run | comment-node | CDATA | element
//   text-run  := (any char except '<')+ with '&' entity decoding
//
// S is space/tab/CR/LF. Both entry points share one TryParse core:
//   * XmlParse -- the T1 spec API: throws XmlParseException with a
//     "line L, column C: reason" message on malformed input.
//   * il2cpp_xml_parse -- the T2/T3 contract API: returns 0 and leaves
//     the output untouched on malformed input (never throws).
//------------------------------------------------------------------------------

#include "xml_ast.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <utility>

namespace {

// Failure reason phrases -- the "reason" part of the exception message.
// (Substrings asserted by the T1 spec; keep them stable.)
const char* const kEmpty = "empty document";
const char* const kUnterminatedElement = "unterminated element";
const char* const kMismatchedEndTag = "mismatched end tag";
const char* const kMustBeQuoted = "must be quoted";
const char* const kUnterminatedAttrValue = "unterminated attribute value";
const char* const kInvalidAttrChar = "invalid character in attribute value";
const char* const kDuplicateAttr = "duplicate attribute";
const char* const kUnknownEntity = "unknown entity";
const char* const kUnterminatedEntity = "unterminated entity";
const char* const kMalformedNumeric = "malformed numeric entity";
const char* const kOutOfRange = "out of range";
const char* const kDoctype = "DOCTYPE";
const char* const kProcessingInstruction = "processing instruction";
const char* const kContentAfterRoot = "content after root";
const char* const kUnexpectedEndTag = "unexpected end tag";
const char* const kInvalidName = "invalid element name";
const char* const kUnterminatedComment = "unterminated comment";
const char* const kUnterminatedCdata = "unterminated CDATA";
const char* const kUnterminatedDeclaration = "unterminated declaration";

struct Error
{
    const char* reason = nullptr;
    int line = 1;
    int column = 1;
};

// Parse cursor: bytes plus 1-based line/column tracking for error messages.
struct Cursor
{
    const char* p;
    const char* end;
    int line;
    int column;
    Error error;
    bool failed;
};

// Record the FIRST failure only (deepest cause wins; later callers must
// not overwrite it).
void FailAt(Cursor& c, const char* reason, int line, int column)
{
    if (!c.failed)
    {
        c.error.reason = reason;
        c.error.line = line;
        c.error.column = column;
        c.failed = true;
    }
}

void Fail(Cursor& c, const char* reason)
{
    FailAt(c, reason, c.line, c.column);
}

// Consume one byte, tracking line/column (a '\n' starts a new column 1).
void Advance(Cursor& c)
{
    if (*c.p == '\n')
    {
        ++c.line;
        c.column = 1;
    }
    else
    {
        ++c.column;
    }
    ++c.p;
}

// Consume bytes up to 'pos' (must not point backwards).
void AdvanceTo(Cursor& c, const char* pos)
{
    while (c.p < pos)
    {
        Advance(c);
    }
}

// Jump without position accounting -- ONLY valid for spans that contain no
// '\n' (entity names, the UTF-8 BOM).
void JumpTo(Cursor& c, const char* pos)
{
    c.column += static_cast<int>(pos - c.p);
    c.p = pos;
}

// Find 'needle' (null-terminated, non-empty) in [begin, end). Returns a
// pointer to the first occurrence, or nullptr.
const char* FindSubstring(const char* begin, const char* end, const char* needle)
{
    const size_t n = std::strlen(needle);
    for (const char* q = begin; static_cast<size_t>(end - q) >= n; ++q)
    {
        if (std::memcmp(q, needle, n) == 0)
        {
            return q;
        }
    }
    return nullptr;
}

void SkipSpace(Cursor& c)
{
    while (c.p < c.end && (*c.p == ' ' || *c.p == '\t' || *c.p == '\r' || *c.p == '\n'))
    {
        Advance(c);
    }
}

bool IsNameStart(char ch)
{
    return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch == '_' || ch == ':';
}

bool IsNameChar(char ch)
{
    return IsNameStart(ch) || (ch >= '0' && ch <= '9') || ch == '-' || ch == '.';
}

// Read an element/attribute name at the cursor. False when no name starts
// here (including non-ASCII starts -- ASCII-only names per the domain).
bool ReadName(Cursor& c, std::string& out)
{
    if (c.p >= c.end || !IsNameStart(*c.p))
    {
        return false;
    }
    out.clear();
    while (c.p < c.end && IsNameChar(*c.p))
    {
        out.push_back(*c.p);
        Advance(c);
    }
    return true;
}

// Append the UTF-8 encoding of code point 'cp' (1..4 bytes).
void AppendCodePoint(std::string& out, uint32_t cp)
{
    if (cp < 0x80u)
    {
        out.push_back(static_cast<char>(cp));
    }
    else if (cp < 0x800u)
    {
        out.push_back(static_cast<char>(0xC0u | (cp >> 6)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    }
    else if (cp < 0x10000u)
    {
        out.push_back(static_cast<char>(0xE0u | (cp >> 12)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    }
    else
    {
        out.push_back(static_cast<char>(0xF0u | (cp >> 18)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    }
}

// Decode ONE entity at the cursor (pointing at '&'). Advances past the
// terminating ';' on success. False with a classified reason on failure.
bool DecodeEntity(Cursor& c, std::string& out)
{
    const char* semi = static_cast<const char*>(std::memchr(
        c.p + 1, ';', static_cast<size_t>(c.end - (c.p + 1))));
    if (semi == nullptr || semi == c.p + 1)
    {
        Fail(c, kUnterminatedEntity); // no ';' or "&;"
        return false;
    }
    const size_t len = static_cast<size_t>(semi - (c.p + 1));
    const char* name = c.p + 1;

    static const struct { const char* name; char ch; } kNamed[] = {
        {"amp", '&'}, {"lt", '<'}, {"gt", '>'}, {"quot", '"'}, {"apos", '\''},
    };
    for (const auto& e : kNamed)
    {
        const size_t n = std::strlen(e.name);
        if (n == len && std::memcmp(name, e.name, n) == 0)
        {
            out.push_back(e.ch);
            JumpTo(c, semi + 1);
            return true;
        }
    }

    if (len >= 2 && name[0] == '#') // numeric character reference
    {
        const char* digits = name + 1;
        uint32_t base = 10;
        if (len >= 3 && (digits[0] == 'x' || digits[0] == 'X'))
        {
            base = 16;
            ++digits;
        }
        if (digits == semi)
        {
            Fail(c, kMalformedNumeric); // "&#;" / "&#x;"
            return false;
        }
        uint32_t value = 0;
        for (const char* d = digits; d < semi; ++d)
        {
            uint32_t digit;
            if (*d >= '0' && *d <= '9')
            {
                digit = static_cast<uint32_t>(*d - '0');
            }
            else if (base == 16 && *d >= 'a' && *d <= 'f')
            {
                digit = static_cast<uint32_t>(*d - 'a' + 10);
            }
            else if (base == 16 && *d >= 'A' && *d <= 'F')
            {
                digit = static_cast<uint32_t>(*d - 'A' + 10);
            }
            else
            {
                Fail(c, kMalformedNumeric);
                return false;
            }
            if (value > (0x10FFFFu - digit) / base)
            {
                Fail(c, kOutOfRange); // exceeds the XML character range
                return false;
            }
            value = value * base + digit;
        }
        if (value == 0 || (value >= 0xD800u && value <= 0xDFFFu))
        {
            Fail(c, kOutOfRange); // NUL and surrogates are illegal
            return false;
        }
        AppendCodePoint(out, value);
        JumpTo(c, semi + 1);
        return true;
    }
    Fail(c, kUnknownEntity);
    return false;
}

// Consume a comment section (cursor at "<!--"). Returns true on success,
// having advanced past the terminator. Terminator rule: the FIRST "-->" if
// present; otherwise the FIRST '>' that follows the FIRST '--' (see
// xml_ast.h).
bool ConsumeComment(Cursor& c)
{
    const char* contentStart = c.p + 4;
    const char* consumeTo = nullptr;
    const char* close = FindSubstring(contentStart, c.end, "-->");
    if (close != nullptr)
    {
        consumeTo = close + 3;
    }
    else
    {
        for (const char* q = contentStart; c.end - q >= 2; ++q)
        {
            if (q[0] != '-' || q[1] != '-')
            {
                continue;
            }
            for (const char* r = q + 2; r < c.end; ++r)
            {
                if (*r == '>')
                {
                    consumeTo = r + 1;
                    break;
                }
            }
            break;
        }
    }
    if (consumeTo == nullptr)
    {
        return false;
    }
    AdvanceTo(c, consumeTo);
    return true;
}

// Parse ONE element (the opening '<' is at the cursor). 'node' receives
// kind/name/attributes/children on success.
bool ParseElement(Cursor& c, il2cpp::XmlNode& node)
{
    if (c.p >= c.end || *c.p != '<')
    {
        Fail(c, kInvalidName);
        return false;
    }
    Advance(c);
    if (c.p >= c.end)
    {
        Fail(c, kUnterminatedElement);
        return false;
    }
    if (*c.p == '/')
    {
        Fail(c, kUnexpectedEndTag); // defensive; '</' is intercepted earlier
        return false;
    }
    if (!ReadName(c, node.name))
    {
        Fail(c, kInvalidName);
        return false;
    }
    for (;;)
    {
        SkipSpace(c);
        if (c.p >= c.end)
        {
            Fail(c, kUnterminatedElement);
            return false;
        }
        if (*c.p == '>')
        {
            Advance(c);
            break;
        }
        if (*c.p == '/')
        {
            if (c.p + 1 < c.end && c.p[1] == '>')
            {
                Advance(c);
                Advance(c);
                return true; // self-closing
            }
            Fail(c, kUnterminatedElement);
            return false;
        }
        // attribute
        il2cpp::XmlAttr attr;
        if (!ReadName(c, attr.name))
        {
            Fail(c, kInvalidName);
            return false;
        }
        SkipSpace(c);
        if (c.p >= c.end || *c.p != '=')
        {
            Fail(c, kMustBeQuoted);
            return false;
        }
        Advance(c);
        SkipSpace(c);
        if (c.p >= c.end || (*c.p != '"' && *c.p != '\''))
        {
            Fail(c, kMustBeQuoted); // value must be quoted
            return false;
        }
        const char quote = *c.p;
        Advance(c);
        for (;;)
        {
            if (c.p >= c.end)
            {
                Fail(c, kUnterminatedAttrValue);
                return false;
            }
            const char ch = *c.p;
            if (ch == quote)
            {
                Advance(c);
                break;
            }
            if (ch == '&')
            {
                if (!DecodeEntity(c, attr.value))
                {
                    return false;
                }
                continue;
            }
            if (ch == '<')
            {
                Fail(c, kInvalidAttrChar); // '<' is illegal in a value
                return false;
            }
            attr.value.push_back(ch);
            Advance(c);
        }
        for (const auto& existing : node.attributes)
        {
            if (existing.name == attr.name)
            {
                Fail(c, kDuplicateAttr);
                return false;
            }
        }
        node.attributes.push_back(std::move(attr));
    }

    // content loop (after the opening '>')
    std::string text;
    for (;;)
    {
        if (c.p >= c.end)
        {
            Fail(c, kUnterminatedElement);
            return false;
        }
        if (*c.p == '<')
        {
            if (!text.empty())
            {
                il2cpp::XmlNode tn;
                tn.kind = il2cpp::XmlNodeKind::Text;
                tn.text = std::move(text);
                node.children.push_back(std::move(tn));
            }
            if (c.p + 1 < c.end && c.p[1] == '/')
            {
                const int tagLine = c.line;
                const int tagColumn = c.column; // position of the '<'
                Advance(c);
                Advance(c);
                std::string endName;
                if (!ReadName(c, endName))
                {
                    FailAt(c, kInvalidName, tagLine, tagColumn);
                    return false;
                }
                SkipSpace(c);
                if (c.p >= c.end || *c.p != '>')
                {
                    FailAt(c, kUnterminatedElement, tagLine, tagColumn);
                    return false;
                }
                Advance(c);
                if (endName != node.name)
                {
                    FailAt(c, kMismatchedEndTag, tagLine, tagColumn);
                    return false;
                }
                return true;
            }
            if (c.end - c.p >= 4 && std::memcmp(c.p, "<!--", 4) == 0)
            {
                // Comment node. Terminator rule (documented simplification,
                // see xml_ast.h): the FIRST "-->" ends the comment (content
                // excludes its dashes); otherwise the FIRST '>' that follows
                // the FIRST '--' ends it (content includes that '--').
                il2cpp::XmlNode cn;
                cn.kind = il2cpp::XmlNodeKind::Comment;
                const char* contentStart = c.p + 4;
                const char* contentEnd = nullptr;
                const char* consumeTo = nullptr;
                const char* close = FindSubstring(contentStart, c.end, "-->");
                if (close != nullptr)
                {
                    contentEnd = close;
                    consumeTo = close + 3;
                }
                else
                {
                    for (const char* q = contentStart; c.end - q >= 2; ++q)
                    {
                        if (q[0] != '-' || q[1] != '-')
                        {
                            continue;
                        }
                        for (const char* r = q + 2; r < c.end; ++r)
                        {
                            if (*r == '>')
                            {
                                contentEnd = r;
                                consumeTo = r + 1;
                                break;
                            }
                        }
                        break;
                    }
                }
                if (contentEnd == nullptr)
                {
                    Fail(c, kUnterminatedComment);
                    return false;
                }
                cn.text.assign(contentStart, contentEnd);
                node.children.push_back(std::move(cn));
                AdvanceTo(c, consumeTo);
                continue;
            }
            if (c.end - c.p >= 9 && std::memcmp(c.p, "<![CDATA[", 9) == 0)
            {
                const char* close = FindSubstring(c.p + 9, c.end, "]]>");
                if (close == nullptr)
                {
                    Fail(c, kUnterminatedCdata);
                    return false;
                }
                il2cpp::XmlNode dn;
                dn.kind = il2cpp::XmlNodeKind::CDATA;
                dn.text.assign(c.p + 9, close);
                node.children.push_back(std::move(dn));
                AdvanceTo(c, close + 3);
                continue;
            }
            if (c.end - c.p >= 9 && std::memcmp(c.p, "<!DOCTYPE", 9) == 0)
            {
                Fail(c, kDoctype); // unsupported
                return false;
            }
            if (c.p + 1 < c.end && c.p[1] == '?')
            {
                Fail(c, kProcessingInstruction); // unsupported
                return false;
            }
            il2cpp::XmlNode child;
            if (!ParseElement(c, child))
            {
                return false;
            }
            node.children.push_back(std::move(child));
            continue;
        }
        if (*c.p == '&')
        {
            if (!DecodeEntity(c, text))
            {
                return false;
            }
            continue;
        }
        text.push_back(*c.p);
        Advance(c);
    }
}

// Shared TryParse core. On failure, 'outError' carries the FIRST recorded
// failure (reason + position).
bool TryParseImpl(const char* text, size_t length, il2cpp::XmlNode& outRoot,
                  Error& outError)
{
    if (text == nullptr)
    {
        outError.reason = kEmpty;
        outError.line = 1;
        outError.column = 1;
        return false;
    }
    Cursor c{text, text + length, 1, 1, Error{}, false};
    if (length == 0)
    {
        Fail(c, kEmpty);
        outError = c.error;
        return false;
    }
    // optional UTF-8 BOM (skipped without position effect)
    if (c.end - c.p >= 3 && static_cast<unsigned char>(c.p[0]) == 0xEF &&
        static_cast<unsigned char>(c.p[1]) == 0xBB &&
        static_cast<unsigned char>(c.p[2]) == 0xBF)
    {
        JumpTo(c, c.p + 3);
    }
    // leading declaration / top-level comments / whitespace, in any order
    for (;;)
    {
        SkipSpace(c);
        if (c.end - c.p >= 5 && std::memcmp(c.p, "<?xml", 5) == 0)
        {
            const char* close = FindSubstring(c.p + 5, c.end, "?>");
            if (close == nullptr)
            {
                Fail(c, kUnterminatedDeclaration);
                outError = c.error;
                return false;
            }
            AdvanceTo(c, close + 2);
            continue;
        }
        if (c.end - c.p >= 4 && std::memcmp(c.p, "<!--", 4) == 0)
        {
            if (!ConsumeComment(c))
            {
                Fail(c, kUnterminatedComment);
                outError = c.error;
                return false;
            }
            continue;
        }
        if (c.end - c.p >= 2 && c.p[0] == '<' && c.p[1] == '?')
        {
            Fail(c, kProcessingInstruction);
            outError = c.error;
            return false;
        }
        break;
    }
    if (c.p >= c.end || *c.p != '<')
    {
        Fail(c, kEmpty); // no root element
        outError = c.error;
        return false;
    }
    if (c.p + 1 < c.end && c.p[1] == '/')
    {
        Fail(c, kUnexpectedEndTag); // lone end tag
        outError = c.error;
        return false;
    }
    il2cpp::XmlNode root;
    if (!ParseElement(c, root))
    {
        outError = c.error;
        return false;
    }
    // trailing whitespace / top-level comments
    for (;;)
    {
        SkipSpace(c);
        if (c.end - c.p >= 4 && std::memcmp(c.p, "<!--", 4) == 0)
        {
            if (!ConsumeComment(c))
            {
                Fail(c, kUnterminatedComment);
                outError = c.error;
                return false;
            }
            continue;
        }
        break;
    }
    if (c.p != c.end)
    {
        Fail(c, kContentAfterRoot);
        outError = c.error;
        return false;
    }
    outRoot = std::move(root);
    return true;
}

} // namespace

namespace il2cpp { namespace runtime {

XmlParseException::XmlParseException(int line, int column, const char* reason)
{
    char buffer[192];
    std::snprintf(buffer, sizeof(buffer), "line %d, column %d: %s", line, column,
                  reason != nullptr ? reason : "");
    message_ = buffer;
}

const char* XmlParseException::what() const noexcept
{
    return message_.c_str();
}

XmlNode XmlParse(const char* text)
{
    return XmlParse(text, text != nullptr ? std::strlen(text) : 0);
}

XmlNode XmlParse(const char* text, std::size_t length)
{
    il2cpp::XmlNode root;
    Error error;
    if (!TryParseImpl(text, length, root, error))
    {
        throw XmlParseException(error.line, error.column, error.reason);
    }
    return root;
}

int32_t il2cpp_xml_parse(const char* text, il2cpp::XmlNode* outRoot)
{
    if (outRoot == nullptr)
    {
        return 0;
    }
    Error error;
    return TryParseImpl(text, text != nullptr ? std::strlen(text) : 0, *outRoot,
                        error)
               ? 1
               : 0;
}

const XmlAttr* xml_find_attribute(const XmlNode& node, const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    for (const XmlAttr& attr : node.attributes)
    {
        if (attr.name == name)
        {
            return &attr;
        }
    }
    return nullptr;
}

const XmlNode* xml_first_element_child(const XmlNode& node)
{
    for (const XmlNode& child : node.children)
    {
        if (child.kind == XmlNodeKind::Element)
        {
            return &child;
        }
    }
    return nullptr;
}

std::string xml_inner_text(const XmlNode& node)
{
    std::string out;
    for (const XmlNode& child : node.children)
    {
        if (child.kind == XmlNodeKind::Text || child.kind == XmlNodeKind::CDATA)
        {
            out += child.text;
        }
        else
        {
            out += xml_inner_text(child);
        }
    }
    return out;
}

}} // namespace il2cpp::runtime
