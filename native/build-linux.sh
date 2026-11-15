#!/usr/bin/env sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
mkdir -p "$ROOT/runtimes/linux-x64/native"
gcc --version | head -n 1
gcc -std=c11 -O2 -fPIC -fvisibility=hidden -shared \
  "$ROOT/native/phonestek_native.c" \
  -o "$ROOT/runtimes/linux-x64/native/libphonestek_native.so"
echo "Built runtimes/linux-x64/native/libphonestek_native.so"
