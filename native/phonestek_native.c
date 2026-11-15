#include "phonestek_native.h"

/* Deterministic integer score. Valid: RAM 1..1024 GB, storage 1..1,000,000 GB,
   battery 1..100,000 mAh. Invalid input returns -1. */
PHONESTEK_EXPORT int32_t phonestek_phone_score(int32_t ram_gb, int32_t storage_gb, int32_t battery_mah)
{
    if (ram_gb < 1 || ram_gb > 1024 || storage_gb < 1 || storage_gb > 1000000 ||
        battery_mah < 1 || battery_mah > 100000) return -1;
    return ram_gb * 100 + storage_gb / 10 + battery_mah / 100;
}
