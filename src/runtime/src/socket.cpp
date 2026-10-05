//------------------------------------------------------------------------------
// socket.cpp -- the self-contained Windows/POSIX dual branch behind socket.h.
//
// The socket domain is deliberately NOT part of the platform layer (see
// docs/design/cross-platform-design.md section 2): all platform divergence
// lives in this one translation unit as #if defined(_WIN32) / #else pairs.
//
//   Windows: winsock2. WSAStartup(2,2) is lazy and runs exactly once per
//            process (std::call_once; no WSACleanup -- winsock state lives
//            for the process lifetime). The ws2_32 link dependency is
//            self-contained through #pragma comment(lib, "ws2_32.lib"),
//            so no CMakeLists change is needed on any toolchain (MSVC /
//            MinGW-gcc / llvm-mingw all honor the .drectve directive).
//   POSIX:   socket/connect/send/recv/getpeername/getsockname/close.
//            send passes MSG_NOSIGNAL where defined (Linux/Android); on
//            Apple (no MSG_NOSIGNAL) SO_NOSIGPIPE is set per socket at
//            create -- a write to a peer that closed must never SIGPIPE
//            the host process.
//
// Both branches share one host-resolution path: getaddrinfo with
// AF_INET + SOCK_STREAM hints (IPv4-only simplified model; 'host' may be a
// dotted-quad literal or a name like "localhost").
//------------------------------------------------------------------------------

#include "socket.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <new>

#if defined(_WIN32)
    #if !defined(_WIN32_WINNT)
        #define _WIN32_WINNT 0x0600 // Vista+ winsock (getaddrinfo/inet_ntop)
    #endif
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <winsock2.h>
    #include <ws2tcpip.h>
    // MSVC honors #pragma comment(lib) (self-contained link). GCC16 warns
    // -Wunknown-pragmas and llvm-mingw clang warns "unknown pragma", so both
    // link with an explicit -lws2_32 (CMakeLists PUBLIC ws2_32).
    #if defined(_MSC_VER)
        #pragma comment(lib, "ws2_32.lib")
    #endif
    #include <mutex>
#else
    #include <sys/types.h> // socklen_t source on strict BSD toolchains
    #include <sys/socket.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <netdb.h>
    #include <unistd.h>
#endif

namespace il2cpp { namespace runtime {

struct il2cpp_socket {
    intptr_t handle; // Windows: SOCKET (UINT_PTR); POSIX: int fd. -1 = invalid.
};

namespace {

#if defined(_WIN32)
SOCKET SockFrom(const il2cpp_socket* sock)
{
    return static_cast<SOCKET>(sock->handle);
}
#else
int SockFrom(const il2cpp_socket* sock)
{
    return static_cast<int>(sock->handle);
}
#endif

#if defined(_WIN32)
// WSAStartup is reference-counted by the OS, so the test's raw-winsock
// server side may call it independently; the runtime side runs exactly
// once via call_once.
std::once_flag g_wsa_startup_flag;
bool g_wsa_ok = false;

void WsaStartupOnce()
{
    WSADATA wsaData;
    g_wsa_ok = (::WSAStartup(MAKEWORD(2, 2), &wsaData) == 0);
}

bool EnsureWsaStarted()
{
    std::call_once(g_wsa_startup_flag, WsaStartupOnce);
    return g_wsa_ok;
}
#endif

} // namespace

il2cpp_socket* il2cpp_socket_create()
{
#if defined(_WIN32)
    if (!EnsureWsaStarted())
    {
        return nullptr;
    }
    const SOCKET fd = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (fd == INVALID_SOCKET)
    {
        return nullptr;
    }
#else
    const int fd = ::socket(AF_INET, SOCK_STREAM, 0);
    if (fd < 0)
    {
        return nullptr;
    }
#endif
    il2cpp_socket* sock = new (std::nothrow) il2cpp_socket;
    if (sock == nullptr)
    {
#if defined(_WIN32)
        ::closesocket(fd);
#else
        ::close(fd);
#endif
        return nullptr;
    }
    sock->handle = static_cast<intptr_t>(fd);
#if !defined(_WIN32) && defined(SO_NOSIGPIPE)
    const int one = 1;
    ::setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &one, sizeof(one));
#endif
    return sock;
}

bool il2cpp_socket_connect(il2cpp_socket* sock, const char* host,
                           uint16_t port)
{
    if (sock == nullptr || host == nullptr || host[0] == '\0')
    {
        return false;
    }

    struct addrinfo hints;
    std::memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_INET; // IPv4-only simplified model
    hints.ai_socktype = SOCK_STREAM;

    char portText[16];
    std::snprintf(portText, sizeof(portText), "%u",
                  static_cast<unsigned>(port));

    struct addrinfo* result = nullptr;
    if (::getaddrinfo(host, portText, &hints, &result) != 0
        || result == nullptr)
    {
        if (result != nullptr)
        {
            ::freeaddrinfo(result);
        }
        return false;
    }
#if defined(_WIN32)
    const int rc = ::connect(SockFrom(sock), result->ai_addr,
                             static_cast<int>(result->ai_addrlen));
#else
    const int rc = ::connect(SockFrom(sock), result->ai_addr,
                             result->ai_addrlen);
#endif
    ::freeaddrinfo(result);
    return rc == 0;
}

int32_t il2cpp_socket_send(il2cpp_socket* sock, const void* bytes,
                           int32_t count)
{
    if (sock == nullptr || bytes == nullptr || count < 0)
    {
        return -1;
    }
    if (count == 0)
    {
        return 0;
    }
#if defined(_WIN32)
    const int rc = ::send(SockFrom(sock), static_cast<const char*>(bytes),
                          count, 0);
#else
    int flags = 0;
#if defined(MSG_NOSIGNAL)
    flags = MSG_NOSIGNAL;
#endif
    const ssize_t rc = ::send(SockFrom(sock), bytes,
                              static_cast<size_t>(count), flags);
#endif
    return rc < 0 ? -1 : static_cast<int32_t>(rc);
}

int32_t il2cpp_socket_receive(il2cpp_socket* sock, void* buffer,
                              int32_t count)
{
    if (sock == nullptr || buffer == nullptr || count < 0)
    {
        return -1;
    }
    if (count == 0)
    {
        return 0;
    }
#if defined(_WIN32)
    const int rc = ::recv(SockFrom(sock), static_cast<char*>(buffer),
                          count, 0);
#else
    const ssize_t rc = ::recv(SockFrom(sock), buffer,
                              static_cast<size_t>(count), 0);
#endif
    return rc < 0 ? -1 : static_cast<int32_t>(rc);
}

bool il2cpp_socket_get_remote_endpoint(il2cpp_socket* sock, char* ipOut,
                                       int32_t ipOutLen, uint16_t* portOut)
{
    if (sock == nullptr || ipOut == nullptr || ipOutLen < 16
        || portOut == nullptr)
    {
        return false;
    }
    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
#if defined(_WIN32)
    int addrLen = static_cast<int>(sizeof(addr));
#else
    socklen_t addrLen = sizeof(addr);
#endif
    if (::getpeername(SockFrom(sock),
                      reinterpret_cast<struct sockaddr*>(&addr),
                      &addrLen) != 0)
    {
        return false;
    }
    *portOut = ntohs(addr.sin_port);
    return ::inet_ntop(AF_INET, &addr.sin_addr, ipOut,
                       static_cast<size_t>(ipOutLen)) != nullptr;
}

bool il2cpp_socket_get_local_endpoint(il2cpp_socket* sock, char* ipOut,
                                      int32_t ipOutLen, uint16_t* portOut)
{
    if (sock == nullptr || ipOut == nullptr || ipOutLen < 16
        || portOut == nullptr)
    {
        return false;
    }
    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
#if defined(_WIN32)
    int addrLen = static_cast<int>(sizeof(addr));
#else
    socklen_t addrLen = sizeof(addr);
#endif
    if (::getsockname(SockFrom(sock),
                      reinterpret_cast<struct sockaddr*>(&addr),
                      &addrLen) != 0)
    {
        return false;
    }
    *portOut = ntohs(addr.sin_port);
    return ::inet_ntop(AF_INET, &addr.sin_addr, ipOut,
                       static_cast<size_t>(ipOutLen)) != nullptr;
}

void il2cpp_socket_close(il2cpp_socket* sock)
{
    if (sock == nullptr)
    {
        return;
    }
#if defined(_WIN32)
    ::closesocket(SockFrom(sock));
#else
    ::close(SockFrom(sock));
#endif
    delete sock;
}

}} // namespace il2cpp::runtime
