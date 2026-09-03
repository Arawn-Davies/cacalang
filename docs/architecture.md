# How the compiler works

Five stages, each in its own folder, each producing something the next one
reads. Nothing reaches back.

```
source text
   │  Syntax/Lexer.cs
   ▼  tokens, each with a position
   │  Syntax/Parser.cs
   ▼  a syntax tree
   │  Binding/TypeChecker.cs
   ▼  the same tree, with a type on every expression and a symbol on every name
   ├──────────────┬───────────────────────┐
   │ Runtime/     │ Emit/IlEmitter.cs     │ (LanguageServer reads the tree too)
   │ Interpreter  │ a .NET assembly       │
   ▼ runs it      ▼ + symbols + launcher  ▼ answers an editor's questions
```

## Diagnostics, not exceptions

Every stage reports problems into a `DiagnosticBag` and carries on. That is why
a run can report several errors, and why the language server can show errors for
a file that is half-typed and does not parse.

Each diagnostic has a position and a stable code; see
[`diagnostics.md`](diagnostics.md).

## Lexer

Reads the source as a string rather than a stream, which is what makes
lookahead and source positions possible. Produces `Token`s carrying a kind, the
text, a decoded value for literals, and a `SourceLocation`.

`SourceLocation` records a start, a length, and **both** a start and an end line
and column. The end matters: a span covering a loop or a function crosses lines,
and its last line cannot be recovered by adding a length to a column.

## Parser

Recursive descent, with precedence climbing for expressions. One method per
construct, and a small table giving each binary operator its precedence:

```
||  &&  ==/!=  </<=/>/>=  +/-  */ /%  unary  primary
```

All binary operators are left associative, which is why `10 - 2 - 3` is 5.

On an error it reports, then skips to the next statement boundary so the rest of
the file is still parsed. Where a token is missing it synthesizes one, so later
stages see a well-shaped tree.

## Type checker

One pass over the tree. It:

- collects every function signature **first**, so a call can name a function
  declared further down, or the one it is in;
- resolves the type of every expression and records it on the node;
- decides each `int` to `float` widening once and records it on the node, so
  the two backends cannot disagree about one;
- binds every name to a symbol carrying its type and where it was declared, and
  records every position a name appears.

That last part is what the language server answers hover, go-to-definition and
find-references from. The compiler itself does not need it.

## Interpreter

Walks the checked tree. Control flow is threaded through a returned `Flow` value
rather than exceptions, so `break`, `continue` and `return` cost no more than
the loops and calls containing them.

It runs the program on a thread whose stack size it chooses, so that a call
depth limit — not a stack overflow — is what stops runaway recursion. A stack
overflow cannot be caught and would take the process down.

## IL emitter

Writes a real .NET assembly with `PersistedAssemblyBuilder`. Every method is
declared before any body is emitted, which is what lets a call reference a
function defined later.

It also writes:

- **a portable PDB**, with a sequence point on each statement and names on the
  locals, so a debugger can step through the `.caca` source;
- **a native launcher**, produced from the apphost template the SDK ships, so
  the output can be run directly rather than through `dotnet`;
- **a runtime configuration**, without which the host does not know which
  runtime to load.

One method is generated into every assembly: the float formatter. A compiled
program cannot call into the compiler, and the interpreter and the compiled
program have to print `1.0` the same way, so the rule is emitted alongside.

## The C backend

`Emit/CEmitter.cs` renders the same type-checked tree as a single,
self-contained C file, built with any C compiler: `caca build --target c`.
Generated code touches the world only through the `caca_` runtime functions
emitted at the top of the file (`Emit/CRuntime.cs`), so a freestanding
runtime — one that boots without an operating system — only has to supply that
contract, not libc. Extern functions are .NET methods, so this target rejects
them.

The fiddly parts are the ones .NET otherwise does quietly: int arithmetic is
carried out on the unsigned representation because signed overflow is
undefined behaviour in C, and the float formatter reproduces .NET's shortest
round-trip text — digits found by rounding to 1..17 significant places until
one reads back as the same value, laid out with .NET's fixed-versus-scientific
thresholds.

`CRuntime.cs` holds two implementations of the same `caca_*` contract: a
hosted one built on libc, and a freestanding one that is not — a PS/2
keyboard driver, a VGA text buffer, a serial port, and a bump allocator,
built without an operating system beneath any of it. [`boot/`](../boot/README.md)
turns a `--target c-freestanding` file into a GRUB-bootable ISO and boots it
in QEMU, headless or in a window. Floats and extern functions are not part of
that runtime yet; see `boot/README.md`.

## The backends must agree

Every language feature is implemented in each backend, and a set of parity
tests runs the same program through all of them and compares the output. This
is the single most useful class of test in the project: it is what catches an
emitter that produces IL the runtime rejects, or C a compiler refuses, or that
quietly computes something different.

## Language server

`src/Caca.LanguageServer` speaks LSP over stdin and stdout. It compiles the
whole file on every keystroke — programs here are small enough for that to be
imperceptible, and it means the editor sees exactly what the compiler sees,
rather than a second, incremental analysis that could drift out of agreement
with it.

The JSON-RPC framing is written out rather than taken from a package: it is a
few dozen lines, and this project is meant to be read.

## Stage by file

| Stage | Where |
|---|---|
| Lexer | `src/Caca.Compiler/Syntax/Lexer.cs` |
| Parser | `src/Caca.Compiler/Syntax/Parser.cs` |
| Type checker | `src/Caca.Compiler/Binding/TypeChecker.cs` |
| Interpreter | `src/Caca.Compiler/Runtime/Interpreter.cs` |
| IL emitter, and the symbols it writes | `src/Caca.Compiler/Emit/IlEmitter.cs` |
| C emitter and its runtime | `src/Caca.Compiler/Emit/CEmitter.cs`, `Emit/CRuntime.cs` |
| The native launcher | `src/Caca.Compiler/Emit/AppHost.cs` |
| Language server | `src/Caca.LanguageServer/` |

## Where things are

| Path | What is in it |
|---|---|
| `src/Caca.Compiler/Syntax` | Lexer, parser, tokens, the syntax tree |
| `src/Caca.Compiler/Binding` | Types, symbols, the type checker |
| `src/Caca.Compiler/Diagnostics` | Locations, codes, the bag |
| `src/Caca.Compiler/Runtime` | The interpreter |
| `src/Caca.Compiler/Emit` | The IL and C emitters, and the apphost writer |
| `src/Caca.LanguageServer` | LSP server and protocol |
| `src/Caca.Cli` | The `caca` command and the REPL |
| `editors/vscode` | The VS Code extension |
| `tests/Caca.Tests` | Everything above, tested |
