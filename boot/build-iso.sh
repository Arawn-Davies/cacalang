#!/bin/sh
# Runs inside the toolchain container: compiles /work/program.c against the
# boot stub and writes the bootable ISO to /work/out/os.iso. Used by
# run-qemu-gui.sh, which mounts /work/out and boots the ISO with the host's
# own QEMU so it gets a real window instead of a container's stdio.

set -e

CFLAGS="-m32 -ffreestanding -fno-stack-protector -fno-pic -fno-builtin -O2"

mkdir -p /work/iso/boot/grub /work/out
gcc $CFLAGS -c /boot-glue/boot.s -o /work/boot.o
gcc $CFLAGS -c /work/program.c -o /work/program.o
gcc -m32 -nostdlib -no-pie -T /boot-glue/linker.ld /work/boot.o /work/program.o -o /work/iso/boot/kernel.elf -lgcc
cp /boot-glue/grub.cfg /work/iso/boot/grub/

grub-mkrescue -o /work/out/os.iso /work/iso >/dev/null 2>&1
