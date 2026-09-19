//------------------------------------------------------------------------------
// il2cpp_netsocket.cpp -- System.Net.Sockets.Socket object-layer icalls.
// Reuses the socket.cpp dual-branch pattern (Windows Winsock2 / POSIX BSD).
//------------------------------------------------------------------------------

#include "il2cpp_netsocket.h"

#include "il2cpp_runtime.h"        // il2cpp_class_register
#include "il2cpp_exception_ext.h"  // il2cpp_throw_null_reference_exception

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
    #if !defined(_WIN32_WINNT)
        #define _WIN32_WINNT 0x0600
    #endif
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <winsock2.h>
    #include <ws2tcpip.h>
    #if defined(_MSC_VER)
        #pragma comment(lib, "ws2_32.lib")
    #endif
    #include <mutex>
#else
    #include <sys/types.h>
    #include <sys/socket.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <netdb.h>
    #include <unistd.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

// The fd sits at offset 8 (slot 1); no reference fields (bitmap 0).
static_assert(offsetof(Il2CppSocketObject, fd) == 1 * sizeof(void*),
    "netsocket: fd must sit at slot 1");

bool g_socketClassRegistered = false;

void EnsureSocketClassRegistered()
{
    if (g_socketClassRegistered) return;
    il2cpp_class_register(&System_Net_Sockets_Socket_class);
    g_socketClassRegistered = true;
}

Il2CppSocketObject* AsSocket(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp::il2cpp_throw_null_reference_exception(nullptr);
    }
    return reinterpret_cast<Il2CppSocketObject*>(self);
}

#if defined(_WIN32)
SOCKET SocketFrom(Il2CppSocketObject* s)
{
    // SOCKET is an integer type (UINT_PTR): static_cast both directions.
    return static_cast<SOCKET>(static_cast<intptr_t>(s->fd));
}
#else
int SocketFrom(Il2CppSocketObject* s)
{
    return s->fd;
}
#endif

#if defined(_WIN32)
std::once_flag g_wsaStartupFlag;
bool g_wsaOk = false;
void WsaStartupOnce()
{
    WSADATA wsaData;
    g_wsaOk = (::WSAStartup(MAKEWORD(2, 2), &wsaData) == 0);
}
bool EnsureWsaStarted()
{
    std::call_once(g_wsaStartupFlag, WsaStartupOnce);
    return g_wsaOk;
}
#endif

// Narrow a UTF-16 host name to ASCII for getaddrinfo. Returns false when a
// non-ASCII unit is present (out of M-stage scope -- the caller fails).
bool NarrowHost(const Il2CppString* host, char* out, size_t outSize)
{
    if (host == nullptr || host->length <= 0 ||
        static_cast<size_t>(host->length) >= outSize)
    {
        return false;
    }
    for (int32_t i = 0; i < host->length; ++i)
    {
        char16_t c = host->chars[i];
        if (c >= 0x80) return false;
        out[i] = static_cast<char>(c);
    }
    out[host->length] = '\0';
    return true;
}

} // namespace

}} // namespace il2cpp::runtime

// System.Net.Sockets.Socket runtime descriptor (GLOBAL scope -- the
// transpiler emits &System_Net_Sockets_Socket_class for newobj).
il2cpp::Il2CppClass System_Net_Sockets_Socket_class = {
    "System.Net.Sockets.Socket",           // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppSocketObject)), // instanceSize
    0u,                                     // refFieldBitmap: no references
    0,                                      // refFieldCount
    &System_Object_class,                // baseClass (GLOBAL -- a Socket IS-A object)
    nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

// Socket(AddressFamily, SocketType, ProtocolType) -- create + store the fd.
int32_t icall_socket_ctor(Il2CppObject* self, int32_t family, int32_t type, int32_t protocol)
{
    EnsureSocketClassRegistered();
    Il2CppSocketObject* s = AsSocket(self);
    s->fd = -1; // closed until created
#if defined(_WIN32)
    if (!EnsureWsaStarted()) { return -1; }
    const SOCKET fd = ::socket(family, type, protocol);
    if (fd == INVALID_SOCKET) { return -1; }
    s->fd = static_cast<int32_t>(static_cast<uintptr_t>(fd));
#else
    const int fd = ::socket(family, type, protocol);
    if (fd < 0) { return -1; }
    s->fd = fd;
#if defined(SO_NOSIGPIPE)
    const int one = 1;
    ::setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &one, sizeof(one));
#endif
#endif
    return 0;
}

// Socket.Connect(string host, int port) -- DNS + IPv4 connect.
void icall_socket_connect_host(Il2CppObject* self, Il2CppString* host, int32_t port)
{
    Il2CppSocketObject* s = AsSocket(self);
    char hostText[256];
    if (!NarrowHost(host, hostText, sizeof(hostText))) { return; }

    struct addrinfo hints;
    std::memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;

    char portText[16];
    std::snprintf(portText, sizeof(portText), "%u", static_cast<unsigned>(port));

    struct addrinfo* result = nullptr;
    if (::getaddrinfo(hostText, portText, &hints, &result) != 0 || result == nullptr)
    {
        if (result != nullptr) ::freeaddrinfo(result);
        return;
    }
#if defined(_WIN32)
    ::connect(SocketFrom(s), result->ai_addr, static_cast<int>(result->ai_addrlen));
#else
    ::connect(SocketFrom(s), result->ai_addr, result->ai_addrlen);
#endif
    ::freeaddrinfo(result);
}

// Socket.Send(byte[]) -- loop until full.
int32_t icall_socket_send(Il2CppObject* self, Il2CppArray* buffer)
{
    AsSocket(self);
    if (buffer == nullptr) { return -1; }
    return icall_socket_send_offset(self, buffer, 0, buffer->length);
}

// Socket.Send(byte[], int offset, int count) -- loop until full.
int32_t icall_socket_send_offset(Il2CppObject* self, Il2CppArray* buffer, int32_t offset, int32_t count)
{
    Il2CppSocketObject* s = AsSocket(self);
    if (buffer == nullptr || count == 0) { return count == 0 ? 0 : -1; }
    const char* bytes = static_cast<const char*>(static_cast<const void*>(buffer->data)) + offset;
    int32_t total = 0;
    while (total < count)
    {
#if defined(_WIN32)
        const int rc = ::send(SocketFrom(s), bytes + total, count - total, 0);
#else
        int flags = 0;
#if defined(MSG_NOSIGNAL)
        flags = MSG_NOSIGNAL;
#endif
        const ssize_t rc = ::send(SocketFrom(s), bytes + total,
                                  static_cast<size_t>(count - total), flags);
#endif
        if (rc <= 0) return -1;
        total += static_cast<int32_t>(rc);
    }
    return total;
}

// Socket.Receive(byte[]) -- single recv.
int32_t icall_socket_receive(Il2CppObject* self, Il2CppArray* buffer)
{
    AsSocket(self);
    if (buffer == nullptr) { return -1; }
    return icall_socket_receive_offset(self, buffer, 0, buffer->length);
}

// Socket.Receive(byte[], int offset, int count) -- single recv at offset.
int32_t icall_socket_receive_offset(Il2CppObject* self, Il2CppArray* buffer, int32_t offset, int32_t count)
{
    Il2CppSocketObject* s = AsSocket(self);
    if (buffer == nullptr || count == 0) { return count == 0 ? 0 : -1; }
    char* bytes = static_cast<char*>(static_cast<void*>(buffer->data)) + offset;
#if defined(_WIN32)
    const int rc = ::recv(SocketFrom(s), bytes, count, 0);
#else
    const ssize_t rc = ::recv(SocketFrom(s), bytes, static_cast<size_t>(count), 0);
#endif
    return rc < 0 ? -1 : static_cast<int32_t>(rc);
}

// Socket.Close() -- idempotent.
void icall_socket_close(Il2CppObject* self)
{
    Il2CppSocketObject* s = AsSocket(self);
    if (s->fd == -1) return;
#if defined(_WIN32)
    ::closesocket(SocketFrom(s));
#else
    ::close(SocketFrom(s));
#endif
    s->fd = -1;
}

}} // namespace il2cpp::runtime
