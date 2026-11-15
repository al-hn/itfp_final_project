#ifndef PHONESTEK_NATIVE_H
#define PHONESTEK_NATIVE_H
#include <stdint.h>
#if defined(_WIN32)
#define PHONESTEK_EXPORT __declspec(dllexport)
#else
#define PHONESTEK_EXPORT __attribute__((visibility("default")))
#endif
PHONESTEK_EXPORT int32_t phonestek_phone_score(int32_t ram_gb, int32_t storage_gb, int32_t battery_mah);
#endif
