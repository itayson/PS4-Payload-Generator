#ifndef TAYSON_JOB_WIRE_H
#define TAYSON_JOB_WIRE_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <ctype.h>

/* TAYSON/1 TYJ1: identical field order and limits to Receiver 1.4.2. */
#define TAYSON_MAX_PACKET (96U * 1024U)
typedef struct {
    uint64_t size;
    char url[8192], agent[1024], referer[4096], cookie[8192], headers[8192];
    char content_id[128], name[512], type[32];
} tayson_job;

static uint32_t wire_u32(const unsigned char *p) {
    return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 8) | p[3];
}

static int wire_string(const unsigned char **p, size_t *left, char *out, size_t cap) {
    if (*left < 4) return -1;
    uint32_t n = wire_u32(*p);
    *p += 4; *left -= 4;
    if (n >= cap || n > *left || memchr(*p, 0, n)) return -1;
    memcpy(out, *p, n); out[n] = 0;
    *p += n; *left -= n;
    return 0;
}

static int wire_has_controls(const char *s) {
    for (; *s; ++s) if ((unsigned char)*s < 32 || (unsigned char)*s == 127) return 1;
    return 0;
}

static int wire_parse(const unsigned char *p, size_t n, tayson_job *j) {
    if (n < 16 || n > TAYSON_MAX_PACKET || wire_u32(p) != 0x54594A31U || wire_u32(p + 4) != 1) return -1;
    memset(j, 0, sizeof(*j));
    j->size = ((uint64_t)wire_u32(p + 8) << 32) | wire_u32(p + 12);
    if (!j->size || j->size > INT64_MAX) return -1;
    p += 16; n -= 16;
#define READ_FIELD(f) if (wire_string(&p, &n, j->f, sizeof(j->f))) return -1
    READ_FIELD(url); READ_FIELD(agent); READ_FIELD(referer); READ_FIELD(cookie);
    READ_FIELD(headers); READ_FIELD(content_id); READ_FIELD(name); READ_FIELD(type);
#undef READ_FIELD
    if (n || (!j->content_id[0]) || !j->name[0] || !j->type[0]) return -1;
    size_t prefix = !strncmp(j->url, "https://", 8) ? 8 : !strncmp(j->url, "http://", 7) ? 7 : 0;
    if (!prefix || !j->url[prefix] || j->url[prefix] == '/' || wire_has_controls(j->url)) return -1;
    if (wire_has_controls(j->agent) || wire_has_controls(j->referer) || wire_has_controls(j->cookie)) return -1;
    for (const char *s = j->headers; *s; ++s)
        if ((*s != '\n' && *s != '\t' && (unsigned char)*s < 32) || (unsigned char)*s == 127) return -1;
    if (!j->agent[0]) strcpy(j->agent, "TaysonPS4Payload/1.5.0");
    return 0;
}
#endif
