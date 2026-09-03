# Booting cacalang

`caca build --target c-freestanding` emits C with no dependency on an
operating system: no libc, no linked runtime, nothing but the CPU. This
directory turns that file into something GRUB can boot, and boots it.

```sh
dotnet run --project src/Caca.Cli -- build samples/primes.caca --target c-freestanding -o primes.c
```

## Two ways to run it

**`run-qemu.sh <program.c>`** — headless, scriptable, no host dependency
beyond Docker. Cross-compiler, GRUB and QEMU all live in the container; input
and output go over the emulated serial port, piped to and from the calling
shell. This is the one to reach for from a script or CI.

```sh
boot/run-qemu.sh primes.c
```

**`run-qemu-gui.sh <program.c>`** — a real window, using your own QEMU
(`brew install qemu`) rather than a headless one inside the container.
Cross-compiling and building the ISO still happen in Docker — nothing but
QEMU itself touches the host. Click into the window and type: the kernel
reads its own keyboard through the emulated PS/2 controller, so keystrokes
reach the guest directly, with nothing of this script's in between. The
window stays open once the program halts, so there is something to read.

```sh
boot/run-qemu-gui.sh primes.c
```

## What the freestanding runtime gives a program

`print` writes to both the VGA text buffer (what the window shows) and the
serial port (what a headless run captures) — see `Emit/CRuntime.cs`, whose
freestanding half implements the same `caca_*` contract the hosted one does,
without libc underneath it. `read_int` and `read_string` take one line from
whichever of the keyboard or the serial port produces a byte first, so the
same kernel works typed-into or piped-into without rebuilding it.

Two things a `.caca` program can do elsewhere are not available here yet:

- **Floats.** `read_float`, and printing any float, stop the kernel with an
  honest message rather than a wrong answer. The shortest-round-trip
  formatting the other backends share needs more than this runtime has
  brought along; it is next.
- **`extern` functions.** They are .NET methods; `--target c-freestanding`
  rejects them the same way `--target c` does, with `CACA0025`.

## Why the pieces are shaped the way they are

- **`boot.s`** is the only assembly in the project: the Multiboot header
  GRUB looks for, a stack, and a jump into the runtime's `caca_boot`, which
  sets up the serial port and screen and calls the program's `main`.
- **`linker.ld`** places the kernel at the 1 MiB mark, with the Multiboot
  header first so it falls inside the 8 KiB GRUB searches.
- **`isa-debug-exit`** — the device that lets `run-qemu.sh` end the instant
  the program finishes — is deliberately **not** wired into the GUI script.
  In a window you are watching, ending the VM the moment the program halts
  means the window vanishes before you can read anything; a run that finishes
  fast enough looks exactly like a crash. Without the device, the write
  `caca_stop` makes to reach it lands on nothing, and the halt loop after it
  leaves the last screen up until you close the window yourself.
- **`-serial null`** in the GUI script, rather than piping the serial port
  anywhere: an unattended real port would otherwise cost nothing either, but
  a `-serial none` — dropping the port from the machine entirely, rather than
  attaching an inert backend to it — leaves its I/O ports floating, which on
  this hardware model reads back as all-ones and would be misread as a
  stream of ready bytes.

## A known limitation

Piped input to `run-qemu.sh` can still race the kernel's boot: bytes that
arrive at the emulated UART before the kernel starts polling it are dropped,
since there is no interrupt-driven buffering yet, only polling. A script
piping input should give the boot a moment first. This does not affect the
GUI script, whose input comes from a live keyboard rather than a pipe.
