# The `caca` command

```
caca repl                            Start an interactive prompt
caca run <file.caca>                 Run a program with the interpreter
caca build <file.caca> [-o <path>]   Compile a program to a runnable executable
caca check <file.caca>               Report errors without running anything
caca <file.caca>                     Shorthand for 'caca build'
```

| Option | |
|---|---|
| `-o`, `--output <path>` | Where to write the executable (default: `<file>.exe`) |
| `-r`, `--ref <path>` | A .NET assembly `extern func` targets may bind to; repeat for more than one |
| `--no-launcher` | Emit only the assembly, to be run with `dotnet` |
| `--no-debug` | Do not write debugging symbols |
| `-h`, `--help` | Show help |
| `--version` | Show the compiler version |

Until the compiler is installed as a tool, run it from the repository:

```sh
dotnet run --project src/Caca.Cli -- run samples/helloworld.caca
```

## run

Runs a program with the interpreter, without producing any files. This is the
quickest way to try something, and it behaves identically on Windows, macOS and
Linux.

## check

Compiles without running anything, and reports what it finds:

```
$ caca check broken.caca
broken.caca(2,7): error CACA0010: operator '*' is not defined for types string and int
broken.caca(3,7): error CACA0008: 'y' is not declared; use 'var y = ...' before using it
2 errors.
```

Exits 1 if there are errors, 2 if a program fails while running, and 64 if the
command line itself was wrong. See [`diagnostics.md`](diagnostics.md).

## build

`caca build hello.caca` writes four files:

| File | What it is |
|---|---|
| `hello.exe` | A native launcher you run directly, on Windows, macOS and Linux alike |
| `hello.dll` | The assembly holding the compiled IL |
| `hello.pdb` | Debugging symbols, so a debugger can step through the `.caca` source |
| `hello.runtimeconfig.json` | Which runtime the host should load |

On .NET Framework an `.exe` *was* the assembly. On modern .NET an assembly is
always a `.dll`, and the `.exe` beside it is a small native stub — an
"apphost" — that finds the runtime and hands it the assembly. `caca build`
produces that stub the same way `dotnet build` does, by taking the template that
ships with the SDK and writing the assembly's name into it.

The launcher is built for the machine that produced it. If the template cannot
be found, that is a warning rather than an error: the assembly and its
configuration are still written, and still run with `dotnet hello.dll`.
`--no-launcher` asks for that deliberately.

The runnable file is named `.exe` on every platform, Linux and macOS included,
so that the name is the same everywhere. The assembly beside it has to be a
`.dll`, so the two cannot share one.

## Compiling to C

`--target c` writes the program as one self-contained C file instead of an
assembly, buildable with any C compiler and dependent on nothing else:

```sh
caca build samples/primes.caca --target c -o primes.c
cc primes.c -o primes
./primes
```

The output behaves identically to the other backends — the parity tests hold
it to that. Extern functions are the exception: they are .NET methods, and a
program compiled to C has no .NET to call, so the build reports `CACA0025`
for each one. This target is the first step towards running cacalang without
an operating system underneath it; the generated code reaches the world only
through a small runtime at the top of the file, written so a freestanding
replacement can slot in.

## Referencing a C# assembly

`--ref` names a .NET assembly that [`extern func`](language.md#calling-net)
targets may bind to, on `run`, `check` and `build` alike:

```sh
dotnet build samples/interop
caca run samples/interop/hello.caca --ref samples/interop/bin/Debug/net10.0/Interop.dll
```

`caca build` copies each referenced assembly beside the output, because that is
where the compiled program's references are resolved from. No two of the files
written there may share a name — not the program's and a reference's, and not
two references' — not even by letter case, since on a case-insensitive file
system the copy would overwrite the other file. The build refuses the
combination rather than writing one file over another.

The editor's counterpart of `--ref` is the `cacalang.references` setting: a
list of assembly paths, `${workspaceFolder}` allowed, that the language server
resolves extern targets against. This repository's committed settings already
point it at the Interop sample library. The setting is read when the server
starts, so reload the window after changing it or after building a listed
assembly for the first time.

## Debugging a compiled program

`caca build` writes a portable PDB, so a compiled program can be stepped through
**in its own source**. Each statement carries a sequence point back to the line
it was written on, and locals keep their names, so a debugger shows `total`
rather than `V_0`.

Any .NET debugger will attach. In VS Code, with the C# extension installed:

```json
{
  "type": "coreclr",
  "request": "launch",
  "name": "caca",
  "program": "${workspaceFolder}/hello.dll",
  "justMyCode": false
}
```

The repository ships one of these already; see [`editor.md`](editor.md).
`--no-debug` skips the symbols.

## repl

```
$ caca repl
cacalang REPL. Type a statement, or :help for commands.
caca> var x = 21;
caca> x * 2
42
caca> func twice(n: int): int do
  ...     return n * 2;
  ... end
caca> twice(50)
100
```

A bare expression is printed; anything else runs as a statement. Which of the
two an entry is gets decided by offering it to the compiler both ways, not by
guessing from its text.

Entries carry over, and a definition spanning several lines is read until it is
complete. `:list` shows the session, `:reset` forgets it, `:quit` leaves.

The prompt keeps the text of everything accepted so far and recompiles all of it
on each entry, so there is no incremental path that could behave differently
from the compiler. Input already consumed is replayed rather than read again, so
a session that reads input does not swallow a fresh line every time it reruns.
