#include <stdint.h>
#include "struct.h"

/* Fixed-capacity slots patched by the Android app before the payload is sent
 * to GoldHEN BinLoader. Keep the marker strings unique in the flat payload. */
static char TAYSON_CONTENT_ID[64] = "TAYSON_CONTENT_ID_SLOT_V1";
static char TAYSON_URL[4096] = "TAYSON_URL_SLOT_V1";
static char TAYSON_NAME[256] = "TAYSON_NAME_SLOT_V1";
static char TAYSON_TYPE[16] = "TAYSON_TYPE_V1";
static char TAYSON_SIZE_TEXT[32] = "TAYSON_SIZE_SLOT_V1";
static char TAYSON_CATEGORY[16] = "TAYSON_CAT_V1";

static unsigned long parse_u64_decimal(const char *s) {
    unsigned long value = 0;
    if (!s) return 0;
    for (int i = 0; s[i] >= '0' && s[i] <= '9'; ++i)
        value = value * 10UL + (unsigned long)(s[i] - '0');
    return value;
}

int get_pkg_info(struct bgft_download_param *params) {
    if (!params) return -1;

    params->id = TAYSON_CONTENT_ID;
    params->content_url = TAYSON_URL;
    params->content_ex_url = 0;
    params->content_name = TAYSON_NAME;
    params->icon_path = "";
    params->sku_id = 0;
    params->package_type = TAYSON_TYPE;
    params->package_sub_type = "";
    params->package_size = parse_u64_decimal(TAYSON_SIZE_TEXT);

    /* 2.0.1: do NOT use DISABLE_CDN_QUERY_PARAM here. VikingFile, AkiraBox,
     * Rootz and Mocha can return signed URLs whose query string is required by
     * the CDN. REMOTE marks this as a network source while preserving the URL
     * exactly as supplied by Android. */
    params->option = BGFT_TASK_OPTION_REMOTE;

    /* CATEGORY=gp is a PS4 patch/update. */
    if (TAYSON_CATEGORY[0] == 'g' && TAYSON_CATEGORY[1] == 'p')
        params->option = (enum bgft_task_option_t)(params->option | BGFT_TASK_OPTION_FORCE_UPDATE);

    return 0;
}
