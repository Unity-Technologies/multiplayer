#pragma once

#if defined(WIN32) || defined(_WIN32) || defined(__WIN32__) || defined(_WIN64)
#define PLATFORM_WIN 1
#elif defined(__MACH__) || defined(__APPLE__)
#define PLATFORM_OSX 1
#elif defined(__ANDROID__)
#define PLATFORM_ANDROID 1
#elif defined(__linux__)
#define PLATFORM_LINUX 1
#endif

#include <stdint.h>

#if PLATFORM_WIN
#define WIN32_LEAN_AND_MEAN
#ifdef NETWORK_EXPORTS
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __declspec(dllimport)
#endif
#elif PLATFORM_ANDROID
#define EXPORT_API __attribute__((visibility("default")))
#else
#define EXPORT_API
#endif