/* Host-only SCE test double. Never linked into the PS4 payload. */
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdio.h>
static char url[8192];
static int pos, range_ok, cookie_ok, auth_ok;
int sceNetInit(void) { return 0; }
int sceNetPoolCreate(const char *a,int b,int c) { (void)a;(void)b;(void)c;return 1; }
int sceNetPoolDestroy(int a) { (void)a;return 0; }
int sceKernelLoadStartModule(const char *a,size_t b,const void*c,unsigned d,void*e,int*f) { (void)a;(void)b;(void)c;(void)d;(void)e;(void)f;return 1; }
static int ssl_init(size_t x) { (void)x;return 2; }
static int term(int x) { (void)x;return 0; }
static int http_init(int a,int b,size_t c,int d) { (void)a;(void)b;(void)c;(void)d;return 3; }
static int template_create(int a,const char*b,int c,int d) { (void)a;(void)b;(void)c;(void)d;return 4; }
static int request_create(int a,const char*b,const char*c,uint64_t d) { (void)a;(void)b;(void)d;snprintf(url,sizeof(url),"%s",c);pos=0;range_ok=cookie_ok=auth_ok=0;return 5; }
static int header(int a,const char*b,const char*c,unsigned d) { (void)a;(void)d;if(!strcmp(b,"Range"))range_ok=!strcmp(c,"bytes=0-15");if(!strcmp(b,"Cookie"))cookie_ok=!strcmp(c,"session=fixture");if(!strcmp(b,"Authorization"))auth_ok=!strcmp(c,"Bearer fixture");return 0; }
static int set_flag(int a,int b) { (void)a;(void)b;return 0; }
static int set_timeout(int a,unsigned b) { (void)a;(void)b;return 0; }
static int send_request(int a,const void*b,size_t c) { (void)a;(void)b;(void)c;return range_ok&&cookie_ok&&auth_ok?0:-100; }
static int get_status(int a,int*b) { (void)a;*b=strstr(url,"denied")?403:strstr(url,"ignore-range")?200:206;return 0; }
static int read_data(int a,void*b,size_t n) {
    (void)a;
    const unsigned char good[16]={0x7f,'C','N','T',0,0,0,1,0,0,0,0,0,0,0,0};
    const unsigned char bad[16]="<!DOCTYPE html>";
    if(strstr(url,"truncated") && pos>=5)return 0;
    int count=n<3?(int)n:3;
    memcpy(b,(strstr(url,"html")?bad:good)+pos,(size_t)count);pos+=count;return count;
}
int sceKernelDlsym(int a,const char *s,void **out) {
    (void)a;
#define SYM(name,fn) if(!strcmp(s,name)){*out=(void*)fn;return 0;}
    SYM("sceSslInit",ssl_init) SYM("sceSslTerm",term)
    SYM("sceHttp2Init",http_init) SYM("sceHttp2Term",term)
    SYM("sceHttp2CreateTemplate",template_create) SYM("sceHttp2DeleteTemplate",term)
    SYM("sceHttp2CreateRequestWithURL",request_create) SYM("sceHttp2DeleteRequest",term)
    SYM("sceHttp2AddRequestHeader",header) SYM("sceHttp2SetAutoRedirect",set_flag)
    SYM("sceHttp2SetConnectTimeOut",set_timeout) SYM("sceHttp2SetResolveTimeOut",set_timeout)
    SYM("sceHttp2SetSendTimeOut",set_timeout) SYM("sceHttp2SetRecvTimeOut",set_timeout)
    SYM("sceHttp2SendRequest",send_request) SYM("sceHttp2GetStatusCode",get_status)
    SYM("sceHttp2ReadData",read_data)
#undef SYM
    return -1;
}
