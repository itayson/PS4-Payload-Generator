#ifndef TAYSON_PAYLOAD_COMPAT_H
#define TAYSON_PAYLOAD_COMPAT_H

#include <stddef.h>
#include <stdint.h>

/* ps4-payload-dev/sdk exports this symbol from libkernel but its public
 * headers do not currently declare it. Signature matches sdk/crt/rtld.c. */
int sceKernelLoadStartModule(const char *path, unsigned long args,
                             const void *argp, unsigned int flags,
                             void *pOpt, int *pRes);

#endif
