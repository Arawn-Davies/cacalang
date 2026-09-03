#!/bin/sh
# Boots a --target c-freestanding file in QEMU, via GRUB, inside the
# toolchain container:
#
#   dotnet run --project src/Caca.Cli -- build samples/primes.caca --target c-freestanding -o primes.c
#   boot/run-qemu.sh primes.c
#
# Input typed (or piped) into the terminal reaches the program's read
# statements over the emulated serial port. The whole toolchain lives in
# the container; nothing is installed on the host.

set -e

if [ -z "$1" ] || [ ! -f "$1" ]; then
    echo "usage: boot/run-qemu.sh <program.c>  (a --target c-freestanding output)" >&2
    exit 64
fi

boot_dir=$(cd "$(dirname "$0")" && pwd)
program=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")

docker build --platform linux/amd64 -q -t caca-boot "$boot_dir"

# -i, not -t: input pipes through to the serial port, and QEMU's exit code
# comes back out. The exit status is the debug-exit device's encoding: 1
# after a normal end, 3 after a runtime error.
exec docker run --rm -i --platform linux/amd64 \
    -v "$program":/work/program.c:ro \
    caca-boot
