//------------------------------------------------------------------------------
// http.cpp -- simplified HTTP client engine (GET/POST) on top of the socket
// domain (socket.h). Implements net-domain.md section 2.4:
//
//   * URL parse: scheme://host[:port]/path  (scheme must be http; https and
//     other schemes are rejected with stable errors).
//   * Request: HTTP/1.0 request line + Host header + Connection: close;
//     POST adds Content-Type (application/x-www-form-urlencoded) and
//     Content-Length.
//   * Response: three-stage parse -- status line (200 check), header loop
//     (Content-Length / Transfer-Encoding only), body framing (rule 1:
//     Content-Length N -> read exactly N; rule 2: none -> read to EOF;
//     rule 3: chunked -> loud rejection).
//
// Error reporting follows the socket.h convention: failures are return
// values with a stable English message, never C++ throws. All state is
// per-call (no globals), so the module is thread-safe by construction.
//------------------------------------------------------------------------------

#include "http.h"
#include "socket.h"

#include "il2cpp_http.h"          // the WebClient object layer (this module)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_null / format / not_supported
#include "il2cpp_pinvoke_ext.h"   // managed string <-> heap UTF-8 marshalling
#include "il2cpp_runtime.h"       // il2cpp_class_register / System_Object_class
#include "versionuri.h"           // icall_uri_to_string (DownloadString(Uri))

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

//------------------------------------------------------------------------------
// System.Net.WebClient runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Net_WebClient_class for newobj/isinst/castclass; the same
// contract as System_Version_class in versionuri.cpp). WebClient is
// stateless (net-domain.md section 2.3): the managed object is just the
// header ({ obj } = 8 bytes), no reference fields, base System.Object.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Net_WebClient_class = {
    "System.Net.WebClient",                         // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                             // refFieldBitmap: no reference fields
    0,                                              // refFieldCount
    &System_Object_class,                           // baseClass (a WebClient IS-A object)
    nullptr,                                        // vtable
    0,                                              // vtableSize
    nullptr,                                        // interfaces
    0,                                              // interfaceCount
};

namespace il2cpp { namespace runtime {

namespace {

constexpr int32_t kMaxHeaderLine = 16 * 1024; // header/status line cap
constexpr int32_t kReadChunk = 8192;          // recv chunk size

//------------------------------------------------------------------------------
// URL parsing (strict, net-domain.md section 1.4-8):
//   scheme://host[:port]/path -- scheme must be http (case-insensitive);
//   port 1..65535, default 80; path defaults to "/".
//------------------------------------------------------------------------------
bool StartsWithCI(const char* text, const char* prefix)
{
    for (; *prefix != '\0'; ++text, ++prefix)
    {
        if (*text == '\0'
            || std::tolower(static_cast<unsigned char>(*text))
                   != static_cast<unsigned char>(*prefix))
        {
            return false;
        }
    }
    return true;
}

struct Url {
    std::string host;
    uint16_t port; // 0 = not specified (caller uses 80)
    std::string path;
};

bool ParseUrl(const char* url, Url* out, const char** error)
{
    *error = nullptr;
    if (url == nullptr || url[0] == '\0')
    {
        *error = "null or empty URL";
        return false;
    }
    if (!StartsWithCI(url, "http://"))
    {
        *error = StartsWithCI(url, "https://") ? "https is not supported"
                                               : "malformed URL";
        return false;
    }
    const char* rest = url + 7; // past "http://"
    const char* hostEnd = rest;
    while (*hostEnd != '\0' && *hostEnd != ':' && *hostEnd != '/')
    {
        ++hostEnd;
    }
    if (hostEnd == rest)
    {
        *error = "malformed URL";
        return false;
    }
    out->host.assign(rest, static_cast<size_t>(hostEnd - rest));
    out->port = 0;

    const char* p = hostEnd;
    if (*p == ':')
    {
        ++p;
        const char* digits = p;
        while (*p >= '0' && *p <= '9')
        {
            ++p;
        }
        if (p == digits)
        {
            *error = "malformed URL";
            return false;
        }
        const long value = std::strtol(digits, nullptr, 10);
        if (value < 1 || value > 65535)
        {
            *error = "malformed URL";
            return false;
        }
        out->port = static_cast<uint16_t>(value);
    }

    if (*p == '\0')
    {
        out->path = "/";
    }
    else if (*p == '/')
    {
        out->path = p;
    }
    else
    {
        *error = "malformed URL";
        return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// Buffered line reader over the socket (single-byte recv per byte would
// be correct but wasteful; chunked recv amortizes the syscalls).
//------------------------------------------------------------------------------
struct Reader {
    il2cpp_socket* sock;
    char buf[kReadChunk];
    int32_t len;
    int32_t pos;
    bool eof;    // orderly peer close seen at a buffer boundary
    bool failed; // recv returned an error
};

void InitReader(Reader* r, il2cpp_socket* sock)
{
    r->sock = sock;
    r->len = 0;
    r->pos = 0;
    r->eof = false;
    r->failed = false;
}

int ReadByte(Reader* r)
{
    if (r->pos < r->len)
    {
        return static_cast<unsigned char>(r->buf[r->pos++]);
    }
    if (r->eof)
    {
        return -1;
    }
    const int32_t n = il2cpp_socket_receive(r->sock, r->buf, kReadChunk);
    if (n < 0)
    {
        r->failed = true;
        return -1;
    }
    if (n == 0)
    {
        r->eof = true;
        return -1;
    }
    r->len = n;
    r->pos = 0;
    return static_cast<unsigned char>(r->buf[r->pos++]);
}

// Read one CRLF/LF-terminated line (CR stripped). false on EOF/error --
// the caller picks the message via Reader::failed.
bool ReadLine(Reader* r, std::string* out, const char** error)
{
    out->clear();
    int32_t count = 0;
    for (;;)
    {
        if (count > kMaxHeaderLine)
        {
            *error = "malformed HTTP response";
            return false;
        }
        const int c = ReadByte(r);
        if (c < 0)
        {
            *error = r->failed ? "receive failed" : "malformed HTTP response";
            return false;
        }
        if (c == '\n')
        {
            break;
        }
        out->push_back(static_cast<char>(c));
        ++count;
    }
    if (!out->empty() && out->back() == '\r')
    {
        out->pop_back();
    }
    return true;
}

//------------------------------------------------------------------------------
// Response parsing.
//------------------------------------------------------------------------------
bool ParseStatusLine(const std::string& line, int32_t* code)
{
    if (line.size() < 8 || line.compare(0, 5, "HTTP/") != 0)
    {
        return false;
    }
    const size_t sp = line.find(' ', 5);
    if (sp == std::string::npos)
    {
        return false;
    }
    // exactly three digits, followed by a space or end of line
    if (line.size() < sp + 4)
    {
        return false;
    }
    for (size_t i = sp + 1; i <= sp + 3; ++i)
    {
        if (line[i] < '0' || line[i] > '9')
        {
            return false;
        }
    }
    if (line.size() > sp + 4 && line[sp + 4] != ' ')
    {
        return false;
    }
    *code = (line[sp + 1] - '0') * 100 + (line[sp + 2] - '0') * 10
            + (line[sp + 3] - '0');
    return true;
}

bool HeaderNameEquals(const char* start, const char* colon, const char* name)
{
    const size_t len = std::strlen(name);
    if (static_cast<size_t>(colon - start) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (std::tolower(static_cast<unsigned char>(start[i]))
            != std::tolower(static_cast<unsigned char>(name[i])))
        {
            return false;
        }
    }
    return true;
}

bool ContainsWordCI(const char* text, const char* word)
{
    const size_t wordLen = std::strlen(word);
    for (; *text != '\0'; ++text)
    {
        size_t i = 0;
        while (i < wordLen && text[i] != '\0'
               && std::tolower(static_cast<unsigned char>(text[i]))
                      == static_cast<unsigned char>(word[i]))
        {
            ++i;
        }
        if (i == wordLen)
        {
            return true;
        }
    }
    return false;
}

// Header loop: stop at the blank line. Only Content-Length and
// Transfer-Encoding matter; everything else is dropped.
bool ParseHeaders(Reader* r, int64_t* contentLength, bool* chunked,
                  const char** error)
{
    *contentLength = -1;
    *chunked = false;
    for (;;)
    {
        std::string line;
        if (!ReadLine(r, &line, error))
        {
            return false;
        }
        if (line.empty())
        {
            return true;
        }
        const char* p = line.c_str();
        const char* colon = std::strchr(p, ':');
        if (colon == nullptr)
        {
            continue; // tolerant: skip malformed lines
        }
        if (HeaderNameEquals(p, colon, "Content-Length"))
        {
            const char* value = colon + 1;
            while (*value == ' ' || *value == '\t')
            {
                ++value;
            }
            char* end = nullptr;
            const long long parsed = std::strtoll(value, &end, 10);
            bool ok = end != value;
            for (const char* t = end; ok && *t != '\0'; ++t)
            {
                ok = (*t == ' ' || *t == '\t' || *t == '\r');
            }
            if (ok && parsed >= 0)
            {
                *contentLength = parsed;
            }
        }
        else if (HeaderNameEquals(p, colon, "Transfer-Encoding")
                 && ContainsWordCI(colon, "chunked"))
        {
            *chunked = true;
        }
    }
}

bool ReadBodyExact(Reader* r, int64_t length, std::string* out,
                   const char** error)
{
    out->clear();
    if (length > 0)
    {
        out->reserve(static_cast<size_t>(length));
    }
    for (int64_t i = 0; i < length; ++i)
    {
        const int c = ReadByte(r);
        if (c < 0)
        {
            *error = r->failed ? "receive failed"
                               : "connection closed before body completed";
            return false;
        }
        out->push_back(static_cast<char>(c));
    }
    return true;
}

bool ReadBodyToEof(Reader* r, std::string* out, const char** error)
{
    out->clear();
    for (;;)
    {
        const int c = ReadByte(r);
        if (c < 0)
        {
            if (r->failed)
            {
                *error = "receive failed";
                return false;
            }
            return true; // orderly EOF = complete body
        }
        out->push_back(static_cast<char>(c));
    }
}

//------------------------------------------------------------------------------
// Request building (net-domain.md section 2.4).
//------------------------------------------------------------------------------
std::string BuildRequest(const char* method, const Url& url,
                         const char* body, int32_t bodyLen)
{
    std::string req;
    req.reserve(256 + static_cast<size_t>(bodyLen));
    req += method;
    req += ' ';
    req += url.path;
    req += " HTTP/1.0\r\n";
    req += "Host: ";
    req += url.host;
    if (url.port != 0)
    {
        req += ':';
        req += std::to_string(url.port);
    }
    req += "\r\nConnection: close\r\n";
    if (std::strcmp(method, "POST") == 0)
    {
        req += "Content-Type: application/x-www-form-urlencoded\r\n";
        req += "Content-Length: ";
        req += std::to_string(bodyLen);
        req += "\r\n";
    }
    req += "\r\n";
    if (bodyLen > 0)
    {
        req.append(body, static_cast<size_t>(bodyLen));
    }
    return req;
}

bool SendAll(il2cpp_socket* sock, const char* data, int32_t len)
{
    int32_t sent = 0;
    while (sent < len)
    {
        const int32_t n = il2cpp_socket_send(sock, data + sent, len - sent);
        if (n <= 0)
        {
            return false;
        }
        sent += n;
    }
    return true;
}

il2cpp_http_response DoRequest(const char* method, const char* urlText,
                               const char* body, int32_t bodyLen)
{
    il2cpp_http_response resp = {};
    if (std::strcmp(method, "POST") == 0 && body == nullptr)
    {
        resp.error = "null request body";
        return resp;
    }

    Url url;
    const char* error = nullptr;
    if (!ParseUrl(urlText, &url, &error))
    {
        resp.error = error;
        return resp;
    }
    if (body == nullptr)
    {
        bodyLen = 0;
    }

    const std::string request = BuildRequest(method, url, body, bodyLen);
    il2cpp_socket* sock = il2cpp_socket_create();
    if (sock == nullptr)
    {
        resp.error = "connection failed";
        return resp;
    }
    const uint16_t port = url.port != 0 ? url.port : 80;
    if (!il2cpp_socket_connect(sock, url.host.c_str(), port))
    {
        il2cpp_socket_close(sock);
        resp.error = "connection failed";
        return resp;
    }
    if (!SendAll(sock, request.data(), static_cast<int32_t>(request.size())))
    {
        il2cpp_socket_close(sock);
        resp.error = "send failed";
        return resp;
    }

    Reader reader;
    InitReader(&reader, sock);

    std::string line;
    if (!ReadLine(&reader, &line, &error))
    {
        il2cpp_socket_close(sock);
        resp.error = error;
        return resp;
    }
    int32_t code = 0;
    if (!ParseStatusLine(line, &code))
    {
        il2cpp_socket_close(sock);
        resp.error = "malformed HTTP response";
        return resp;
    }
    resp.status_code = code;

    int64_t contentLength = -1;
    bool chunked = false;
    if (!ParseHeaders(&reader, &contentLength, &chunked, &error))
    {
        il2cpp_socket_close(sock);
        resp.error = error;
        return resp;
    }
    if (chunked)
    {
        il2cpp_socket_close(sock);
        resp.error = "chunked transfer encoding is not supported";
        return resp;
    }

    std::string bodyOut;
    if (contentLength >= 0)
    {
        if (!ReadBodyExact(&reader, contentLength, &bodyOut, &error))
        {
            il2cpp_socket_close(sock);
            resp.error = error;
            return resp;
        }
    }
    else if (!ReadBodyToEof(&reader, &bodyOut, &error))
    {
        il2cpp_socket_close(sock);
        resp.error = error;
        return resp;
    }
    il2cpp_socket_close(sock);

    if (!bodyOut.empty())
    {
        resp.body = static_cast<char*>(std::malloc(bodyOut.size() + 1));
        if (resp.body == nullptr)
        {
            resp.error = "out of memory";
            return resp;
        }
        std::memcpy(resp.body, bodyOut.data(), bodyOut.size());
        resp.body[bodyOut.size()] = '\0';
        resp.body_len = static_cast<int32_t>(bodyOut.size());
    }

    if (code != 200)
    {
        static thread_local char statusBuf[32];
        std::snprintf(statusBuf, sizeof(statusBuf), "HTTP status %d", code);
        resp.error = statusBuf;
        return resp;
    }
    resp.ok = true;
    return resp;
}

} // namespace

il2cpp_http_response il2cpp_http_get(const char* url)
{
    return DoRequest("GET", url, nullptr, 0);
}

il2cpp_http_response il2cpp_http_post(const char* url, const char* body)
{
    const int32_t bodyLen = body == nullptr
                                ? 0
                                : static_cast<int32_t>(std::strlen(body));
    return DoRequest("POST", url, body, bodyLen);
}

void il2cpp_http_free_body(char* body)
{
    std::free(body);
}

//------------------------------------------------------------------------------
// WebClient object layer (net-domain.md section 4.4): the managed-string
// closure over the flat engine. A managed WebClient.DownloadString(url) call
// maps onto a SINGLE entry point -- the emitter cannot compose the five-step
// native sequence (marshal -> get -> ok-check -> UTF-8 -> managed string ->
// free_body) into one call expression.
//------------------------------------------------------------------------------

namespace {

bool g_webClientClassRegistered = false;

// net-domain.md section 2.5 WebException type-name chain:
//   System.Net.WebException -> System.InvalidOperationException ->
//   System.SystemException -> System.Exception (net48 inheritance).
// The factory lives here because exceptionext.cpp is a shared read-only
// domain for this task -- the wiring report asks the exception owner to
// promote this chain to il2cpp_throw_web_exception. [[noreturn]]: the body
// always throws through il2cpp_throw_new (declared [[noreturn]] in
// il2cpp_exception.h), so callers need no terminating return.
[[noreturn]] void ThrowWebException(const char* message)
{
    il2cpp_throw_new(
        "System.Net.WebException,System.Net.WebException,"
        "System.InvalidOperationException,System.SystemException,System.Exception",
        message != nullptr ? message : "The request failed.");
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_web_client_class -- the System.Net.WebClient descriptor (lazily
// registered on first use; idempotent -- il2cpp_class_register dedupes by
// pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_web_client_class()
{
    if (!g_webClientClassRegistered)
    {
        il2cpp_class_register(&System_Net_WebClient_class);
        g_webClientClassRegistered = true;
    }
    return &System_Net_WebClient_class;
}

//------------------------------------------------------------------------------
// icall_web_client_ctor -- no-op ctor (WebClient is stateless).
//------------------------------------------------------------------------------
void icall_web_client_ctor(Il2CppObject* self)
{
    (void)self;
}

//------------------------------------------------------------------------------
// DownloadStringCore -- the shared body of the two download entry points.
//------------------------------------------------------------------------------
Il2CppString* DownloadStringCore(Il2CppString* url)
{
    if (url == nullptr)
    {
        il2cpp_throw_argument_null_exception("address");
    }

    // managed UTF-16 -> UTF-8 C string (the flat engine's input form; full
    // UTF-8 incl. astral pairs -- il2cpp_pinvoke_ext.h contract).
    char* urlUtf8 = il2cpp_pinvoke_string_to_utf8_alloc(url);
    // net48 WebClient.GetUri discriminates by SCHEME PRESENCE before the
    // engine parses: a string with NO "://" separator is not a URL at all --
    // net48 treats it as a CWD-relative FILE path (webcase oracle line 4:
    // "not a url" -> WebException "file not found"). Capture the discriminator
    // before the engine consumes the buffer (webcase gap, web-b23 audit).
    const bool hasSchemeSeparator = std::strstr(urlUtf8, "://") != nullptr;
    const il2cpp_http_response resp = il2cpp_http_get(urlUtf8);
    il2cpp_pinvoke_utf8_free(urlUtf8);

    if (resp.ok)
    {
        // Fixed UTF-8 decode (net-domain.md section 1.4-3). The engine body is
        // NUL-terminated at body_len; an embedded NUL truncates the managed
        // string (documented minimal text surface).
        Il2CppString* body = reinterpret_cast<Il2CppString*>(
            il2cpp_string_new_from_utf8(resp.body != nullptr ? resp.body : ""));
        il2cpp_http_free_body(resp.body);
        return body;
    }

    const char* err = resp.error != nullptr ? resp.error : "request failed";

    // net-domain.md section 2.5 error-to-exception mapping. The engine's
    // stable English error strings (http.h document block) are the
    // discrimination key.
    if (std::strcmp(err, "malformed URL") == 0)
    {
        // net48 WebClient.GetUri: a string WITH a scheme separator is a
        // malformed URL (FormatException, net-domain.md 1.4-8); a string
        // WITHOUT one is not a URL at all -- net48 falls back to a
        // CWD-relative file path and the missing file surfaces as
        // WebException ("file not found", webcase oracle line 4).
        il2cpp_http_free_body(resp.body);
        if (hasSchemeSeparator)
        {
            il2cpp_throw_format_exception(err);
        }
        ThrowWebException("file not found");
    }
    if (std::strcmp(err, "null or empty URL") == 0)
    {
        il2cpp_http_free_body(resp.body);
        il2cpp_throw_format_exception(err);
    }
    if (std::strcmp(err, "https is not supported") == 0
        || std::strcmp(err, "chunked transfer encoding is not supported") == 0)
    {
        il2cpp_http_free_body(resp.body);
        il2cpp_throw_not_supported_exception(err);
    }
    // non-200 status / connect / send / recv / premature EOF -> WebException.
    il2cpp_http_free_body(resp.body);
    ThrowWebException(err);
}

//------------------------------------------------------------------------------
// icall_web_client_download_string -- instance form (net-domain.md section
// 4.4). 'self' is ignored (WebClient carries no state).
//------------------------------------------------------------------------------
Il2CppString* icall_web_client_download_string(Il2CppObject* self,
                                               Il2CppString* url)
{
    (void)self;
    return DownloadStringCore(url);
}

//------------------------------------------------------------------------------
// icall_webclient_download_string -- stateless alias of the download icall
// (task-card form). Same contract as the self-taking entry point.
//------------------------------------------------------------------------------
Il2CppString* icall_webclient_download_string(Il2CppString* url)
{
    return DownloadStringCore(url);
}

//------------------------------------------------------------------------------
// icall_web_client_download_string_uri -- WebClient.DownloadString(Uri)
// (net48). The Uri is marshaled to its string form (icall_uri_to_string --
// net48 WebClient.GetUri(address) returns the Uri's string) and the string
// download closure runs.
//------------------------------------------------------------------------------
Il2CppString* icall_web_client_download_string_uri(Il2CppObject* self,
                                                   Il2CppObject* uri)
{
    if (uri == nullptr)
    {
        // net48 DownloadString((Uri)null) throws ArgumentNullException.
        il2cpp_throw_argument_null_exception("address");
    }
    Il2CppString* url = reinterpret_cast<Il2CppString*>(icall_uri_to_string(uri));
    return icall_web_client_download_string(self, url);
}

}} // namespace il2cpp::runtime
