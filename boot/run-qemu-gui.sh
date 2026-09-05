#!/bin/sh
# Boots a --target c-freestanding file in a real QEMU window, using your own
# QEMU (brew install qemu) rather than a headless one inside a container:
#
#   dotnet run --project src/Caca.Cli -- build samples/primes.caca --target c-freestanding -o primes.c
#   boot/run-qemu-gui.sh primes.c
#
# Cross-compiling and building the ISO still needs the Linux toolchain
# (gcc-multilib, GRUB), which stays in Docker — nothing beyond QEMU touches
# the host. Click into the window once it opens and type: the kernel reads
# its own keyboard through the emulated PS/2 controller, so input goes
# straight from your keypresses to the guest with nothing of this script's
# in between. The serial port is left unattached (-serial null) rather than
# piped anywhere, which is what makes that true.

set -e

if ! command -v qemu-system-i386 >/dev/null 2>&1; then
    echo "error: qemu-system-i386 not found; install QEMU to use the GUI (boot/run-qemu.sh needs no host install)" >&2
    exit 64
fi

if [ -z "$1" ] || [ ! -f "$1" ]; then
    echo "usage: boot/run-qemu-gui.sh <program.c>  (a --target c-freestanding output)" >&2
    exit 64
fi

boot_dir=$(cd "$(dirname "$0")" && pwd)
program=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
out_dir=$(mktemp -d)
trap 'rm -rf "$out_dir"' EXIT

docker build --platform linux/amd64 -q -t caca-boot "$boot_dir"

docker run --rm --platform linux/amd64 \
    --entrypoint /boot-glue/build-iso.sh \
    -v "$program":/work/program.c:ro \
    -v "$out_dir":/work/out \
    caca-boot

echo "booting; click the QEMU window and type directly into it" >&2

# No isa-debug-exit here, unlike the headless script: that device is what
# lets a scripted run end itself the instant the program finishes, which in
# a window you are watching means it vanishes before there is anything to
# read. Without it, the write caca_stop() makes to port 0xf4 reaches no
# device and is simply dropped, and the halt loop after it leaves the last
# screen on display until you close the window.
exec qemu-system-i386 \
    -display cocoa \
    -cdrom "$out_dir/os.iso" \
    -serial null \
    -no-reboot
