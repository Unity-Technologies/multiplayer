#include <network.bindings.h>
#include <stdio.h>
#include <string.h>
#include <assert.h>

#if defined(__APPLE__) || defined(__linux__)
#include <unistd.h>
#include <string.h>
#endif

static int retval;
#define call(function, ...) \
    retval = function(__VA_ARGS__); \
    assert(retval >= SUCCESS);

int main(int argc, char** argv)
{
    printf("sizeof network_address = %d\n", (int)sizeof(network_address));
    printf("sizeof sockaddr_in = %d\n", (int)sizeof(sockaddr_in));
    printf("sizeof sockaddr_in6 = %d\n", (int)sizeof(sockaddr_in6));

    int32_t errorcode;
    network_initialize();
    int64_t server;
    network_address localhost;
    localhost.length = sizeof(sockaddr_in);
    memset(&localhost.addr_in, 0, localhost.length);
    localhost.addr_in.sin_family = AF_INET;
    localhost.addr_in.sin_port = htons(1337);
    localhost.addr_in.sin_addr.s_addr = htonl((127<<24) | 1);
    call(network_create_and_bind, &server, &localhost, &errorcode);
    assert(server != SOCKET_ERROR);

    int64_t client;
    network_address any;
    any.length = sizeof(sockaddr_in);
    memset(&localhost.addr_in, 0, localhost.length);
    any.addr_in.sin_family = AF_INET;
    call(network_create_and_bind, &client, &any, &errorcode);
    assert(client != SOCKET_ERROR);

    network_address server_address;
    server_address.length = sizeof(network_address);
    call(network_get_socket_address, server, &server_address, &errorcode);

    network_address client_address;
    client_address.length = sizeof(network_address);
    call(network_get_socket_address, client, &client_address, &errorcode);

    network_iov_t iov[1];

    const char client_send[] = "Hello Networked World!";
    iov->data = (uint8_t*)client_send;
    iov->length = sizeof(client_send);

    call(network_sendmsg, client, iov, 1, &server_address, &errorcode);

    char server_recv[sizeof(client_send)];
    iov->data = (uint8_t*)server_recv;
    iov->length = sizeof(client_send);

    network_address remote;
    remote.length = sizeof(remote);
    call(network_recvmsg, server, iov, 1, &remote, &errorcode);

    assert(memcmp(client_send, server_recv, sizeof(client_send)) == 0);

    call(network_close, &client, &errorcode);
    assert(client == SUCCESS);
    call(network_close, &server, &errorcode);
    assert(server == SUCCESS);

    network_terminate();

    printf("all passed!\n");

    return 0;
}

#undef call
