#pragma once
//------------------------------------------------------------------------------
// il2cpp_netsocket.h -- System.Net.Sockets.Socket object-layer icall decls.
//
// Object layout: Il2CppSocketObject { Il2CppObject obj; int32_t fd; } -- the
// fd sits at offset 8 (slot 1). fd == -1 means closed. No GC reference
// fields (refFieldBitmap 0).
//
// The seven icalls back MethodMap.Net.cs TryMapSocket's I() strings. The
// GLOBAL System_Net_Sockets_Socket_class descriptor is defined in
// il2cpp_netsocket.cpp and registered lazily by icall_socket_ctor (the
// timer.cpp contract); the transpiler allocates through
// il2cpp_object_new(&System_Net_Sockets_Socket_class) (CodeGenerator.cs).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (receiver type)
#include "il2cpp_string.h"   // Il2CppString (Connect host)
#include "il2cpp_array.h"    // Il2CppArray (Send/Receive buffers)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

#include <cstdint>

// System.Net.Sockets.Socket runtime descriptor (GLOBAL scope -- the
// transpiler emits &System_Net_Sockets_Socket_class for newobj).
extern il2cpp::Il2CppClass System_Net_Sockets_Socket_class;

namespace il2cpp {

// Socket object layout: header + int32 fd at offset 8; -1 = closed.
struct Il2CppSocketObject
{
    Il2CppObject obj; // header (klass pointer, offset 0)
    int32_t fd;       // OS socket fd (offset 8); -1 = closed
};

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// Socket(AddressFamily, SocketType, ProtocolType) -- create the OS socket,
// store the fd; returns 0, or -1 on failure (fd left -1).
int32_t icall_socket_ctor(Il2CppObject* self, int32_t family, int32_t type, int32_t protocol);

// Socket.Connect(string host, int port) -- DNS + IPv4 connect. The managed
// host string is UTF-16; only ASCII host names are supported (non-ASCII
// fails silently -- net48 parity is out of M-stage scope).
void icall_socket_connect_host(Il2CppObject* self, Il2CppString* host, int32_t port);

// Socket.Send(byte[]) -- loop until full; returns bytes sent or -1.
int32_t icall_socket_send(Il2CppObject* self, Il2CppArray* buffer);

// Socket.Send(byte[], int offset, int count) -- loop until full.
int32_t icall_socket_send_offset(Il2CppObject* self, Il2CppArray* buffer, int32_t offset, int32_t count);

// Socket.Receive(byte[]) -- single recv; returns bytes read, 0 on orderly
// shutdown, or -1 on error.
int32_t icall_socket_receive(Il2CppObject* self, Il2CppArray* buffer);

// Socket.Receive(byte[], int offset, int count) -- single recv at offset.
int32_t icall_socket_receive_offset(Il2CppObject* self, Il2CppArray* buffer, int32_t offset, int32_t count);

// Socket.Close() -- idempotent.
void icall_socket_close(Il2CppObject* self);

}} // namespace il2cpp::runtime
