/* The Multiboot stub: what GRUB jumps to, and the only assembly in the
   project. It gives the program a stack and hands over to caca_boot in the
   freestanding runtime, which sets up the serial port and screen and calls
   the program's main. */

.set MAGIC, 0x1badb002
.set FLAGS, 0
.set CHECKSUM, -(MAGIC + FLAGS)

/* The header GRUB looks for in the first 8K of the file; the linker script
   places this section first. */
.section .multiboot
.align 4
.long MAGIC
.long FLAGS
.long CHECKSUM

.section .bss
.align 16
stack_bottom:
.skip 16384
stack_top:

.section .text
.global _start
.type _start, @function
_start:
    cli
    mov $stack_top, %esp
    call caca_boot

    /* caca_boot does not return, but a jump target must exist. */
1:  hlt
    jmp 1b
