# The toolchain that turns a --target c-freestanding file into a bootable
# ISO and runs it: a 32-bit x86 C compiler, GRUB's BIOS image builder, and
# QEMU. Everything stays in this container; nothing is installed on the host.

FROM --platform=linux/amd64 debian:trixie-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
        gcc \
        gcc-multilib \
        grub-pc-bin \
        grub-common \
        xorriso \
        mtools \
        qemu-system-x86 \
    && rm -rf /var/lib/apt/lists/*

COPY boot.s linker.ld grub.cfg build-and-run.sh build-iso.sh /boot-glue/
RUN chmod +x /boot-glue/build-and-run.sh /boot-glue/build-iso.sh

ENTRYPOINT ["/boot-glue/build-and-run.sh"]
