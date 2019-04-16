#pragma once

#include "platform.h"

#if PLATFORM_WIN
#   include <winsock2.h>
#   include <ws2tcpip.h>
#   pragma comment(lib, "ws2_32.lib")
#else
#   include <errno.h>
#   include <netdb.h>
#   define WSAEWOULDBLOCK EAGAIN
#   define WSAECONNRESET ECONNRESET
#   define SOCKET_ERROR -1
#endif

#if PLATFORM_ANDROID
#include <netinet/in.h>
#endif

#define SUCCESS 0

#ifdef __cplusplus
extern "C" {
#endif

typedef struct
{
    union
    {
        struct sockaddr     addr;
        struct sockaddr_in  addr_in;
        struct sockaddr_in6 addr_in6;
    };
    int length;
} network_address;

typedef struct
{
    int32_t length;
    uint8_t *data;
} network_iov_t;

// TODO: Should we name them ..._udp...
EXPORT_API int32_t network_initialize();
EXPORT_API int32_t network_terminate();

EXPORT_API int32_t network_create_and_bind(int64_t *socket_handle, network_address *address, int32_t* errorcode);
EXPORT_API int32_t network_sendmsg(int64_t socket_handle, network_iov_t *iov, int32_t iov_len, network_address *address, int32_t* errorcode);
EXPORT_API int32_t network_recvmsg(int64_t socket_handle, network_iov_t *iov, int32_t iov_len, network_address *remote, int32_t* errorcode);
EXPORT_API int32_t network_close(int64_t *socket_handle, int32_t* errorcode);

EXPORT_API int32_t network_set_nonblocking(int64_t socket_handle);
EXPORT_API int32_t network_set_send_buffer_size(int64_t handle, int size);
EXPORT_API int32_t network_set_receive_buffer_size(int64_t handle, int size);
EXPORT_API int32_t network_set_connection_reset(int64_t handle, int value);
EXPORT_API int32_t network_get_socket_address(int64_t socket_handle, network_address* own_address, int32_t* errorcode);

#ifdef __cplusplus
}
#endif
