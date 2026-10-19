#pragma once

// Including SDKDDKVer.h defines the highest available Windows platform.

// If you wish to build your application for a previous Windows platform, include WinSDKVer.h and
// set the _WIN32_WINNT macro to the platform you wish to support before including SDKDDKVer.h.

// SDKDDKVer.h ships with the Windows SDK / Visual Studio only; when cross
// building with mingw-w64 (linux/win_build.sh) pin the platform by hand.
#ifdef _MSC_VER
#include <SDKDDKVer.h>
#else
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#endif
