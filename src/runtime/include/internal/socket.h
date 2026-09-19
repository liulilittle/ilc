#if defined(__arm__) || defined(_M_ARM) || defined(__i386__) || defined(_M_IX86)
#error "il2cpp runtime: 32-bit targets are not supported in phase 1 (arm64-v8a/x86_64 only)"
#endif

#pragma once
//------------------------------------------------------------------------------
// socket.h -- simplified System.Net.Sockets TCP client sockets (IPv4).
//
// This domain is deliberately NOT part of the platform layer (see
// docs/design/cross-platform-design.md section 2: the plat_ layer covers
// threads/sync/clock/files/dlopen/env/dirs/process -- no sockets). The
// Windows/POSIX divergence lives entirely inside socket.cpp as a self-
// contained dual branch (_WIN32 winsock2 vs. POSIX socket API).
//
// Surface (net48 baseline, simplified where noted):
//   * il2cpp_socket_create: Socket(AddressFamily.InterNetwork,
//     SocketType.Stream, ProtocolType.Tcp). Blocking TCP socket.
//   * il2cpp_socket_connect(host, port): blocking IPv4 connect; 'host' is
//     either a dotted-quad literal ("127.0.0.1") or a host name resolved to
//     its first IPv4 address ("localhost"). net48 resolves DNS, this model
//     stays IPv4-only (AF_INET) by design.
//   * il2cpp_socket_send / il2cpp_socket_receive: one blocking transfer.
//     send returns the bytes accepted by the OS; a short count means the
//     caller must re-send the remainder. receive returns the bytes read;
//     0 = orderly peer close (EOF, like Stream.Read), NOT an error.
//   * il2cpp_socket_get_remote_endpoint / get_local_endpoint: the peer /
//     local IPv4 address as a dotted-quad string plus the port (net48
//     Socket.RemoteEndPoint / LocalEndPoint).
//   * il2cpp_socket_close: releases the handle (Socket.Close). The handle
//     is dangling afterwards -- call exactly once; close(nullptr) is a
//     no-op.
//
// Result conventions (M-stage runtime: failures are return values, never
// C++ throws -- aligned with streamio.h):
//   * create   -> nullptr on failure (winsock init failure, socket() error).
//   * connect  -> false on failure (null/empty host, name resolution
//                 failure, connection refused, unreachable).
//   * send     -> bytes sent; -1 on failure. count < 0 or null handle/buffer
//                 -> -1; count == 0 -> 0.
//   * receive  -> bytes read; 0 at EOF; -1 on failure. count < 0 or null
//                 handle/buffer -> -1; count == 0 -> 0.
//   * endpoints-> true + filled dotted-quad/port; false on failure (null
//                 args, ipOutLen < 16, unconnected socket).
//   * close    -> void; see the lifetime contract above.
//
// Platform mapping (see socket.cpp):
//   Windows: winsock2; WSAStartup(2,2) is lazy and runs exactly once per
//            process (std::call_once; never WSACleanup -- process lifetime
//            winsock state). The ws2_32 link dependency is self-contained
//            via #pragma comment(lib, "ws2_32.lib").
//   POSIX:   socket/connect/send/recv/getpeername/getsockname/close.
//            send uses MSG_NOSIGNAL where defined (Linux/Android); on Apple
//            (no MSG_NOSIGNAL) SO_NOSIGPIPE is set per socket at create, so
//            a write to a closed peer never kills the process.
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

// Opaque socket handle (allocated by create, freed by close). Never
// dereferenced by callers.
struct il2cpp_socket;

// Create a blocking IPv4 TCP socket. Returns nullptr on failure.
il2cpp_socket* il2cpp_socket_create();

// Blocking IPv4 connect to 'host':'port'. 'host' is a dotted-quad literal
// or a name resolved to its first IPv4 address. false on failure.
bool il2cpp_socket_connect(il2cpp_socket* sock, const char* host,
                           uint16_t port);

// Send 'count' bytes from 'bytes'. Returns bytes sent (caller re-sends the
// remainder on a short count), 0 for a zero-length request, -1 on failure.
int32_t il2cpp_socket_send(il2cpp_socket* sock, const void* bytes,
                           int32_t count);

// Receive up to 'count' bytes into 'buffer'. Returns bytes read, 0 at
// orderly peer close (EOF), -1 on failure.
int32_t il2cpp_socket_receive(il2cpp_socket* sock, void* buffer,
                              int32_t count);

// Peer endpoint as a dotted-quad string + port (net48 RemoteEndPoint).
// ipOut needs at least 16 bytes for "255.255.255.255" + NUL. false on
// failure (null args, short buffer, unconnected socket).
bool il2cpp_socket_get_remote_endpoint(il2cpp_socket* sock, char* ipOut,
                                       int32_t ipOutLen, uint16_t* portOut);

// Local endpoint as a dotted-quad string + port (net48 LocalEndPoint).
// Same contract as get_remote_endpoint.
bool il2cpp_socket_get_local_endpoint(il2cpp_socket* sock, char* ipOut,
                                      int32_t ipOutLen, uint16_t* portOut);

// Release the handle. The handle is dangling afterwards; close exactly
// once; close(nullptr) is a no-op.
void il2cpp_socket_close(il2cpp_socket* sock);

}} // namespace il2cpp::runtime
