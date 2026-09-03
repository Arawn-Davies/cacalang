#!/bin/sh
# Runs inside the toolchain container: compiles /work/program.c (a
# --target c-freestanding file mounted by run-qemu.sh) against the boot
# stub, wraps it in a GRUB ISO, and boots it in QEMU with the serial port
# on stdio. QEMU leaves through the debug-exit device when the program
# ends, so a piped run terminates by itself.

set -e

CFLAGS="-m32 -ffreestanding -fno-stack-protector -fno-pic -fno-builtin -O2"

mkdir -p /work/iso/boot/grub
gcc $CFLAGS -c /boot-glue/boot.s -o /work/boot.o
gcc $CFLAGS -c /work/program.c -o /work/program.o
gcc -m32 -nostdlib -no-pie -T /boot-glue/linker.ld /work/boot.o /work/program.o -o /work/iso/boot/kernel.elf -lgcc
cp /boot-glue/grub.cfg /work/iso/boot/grub/

grub-mkrescue -o /work/os.iso /work/iso >/dev/null 2>&1

exec qemu-system-i386 \
    -cdrom /work/os.iso \
    -nographic \
    -no-reboot \
    -device isa-debug-exit,iobase=0xf4,iosize=1
