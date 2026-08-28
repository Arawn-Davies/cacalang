# cacalang

A small, C-like language with a compiler that both interprets programs and
compiles them to real .NET assemblies. Builds and runs on Windows, macOS and
Linux.

```
var ntimes = 0;
print "How much do you love this company? (1-10) ";
read_int ntimes;
for x = 1 to ntimes do
    print "Developers!";
end;
print "Who said sit down?!!!!!";
```

cacalang began as the Good for Nothing compiler that Joel Pobar and Joe Duffy
presented at PDC 2005, published with
[an article in MSDN Magazine](https://msdn.microsoft.com/en-us/magazine/cc136756.aspx).
That compiler targeted .NET Framework 2.0 and could only be built with Visual
Studio on Windows. It has since been rebuilt on .NET 10, given a type checker,
diagnostics with source positions, a second backend and a test suite, and it is
now diverging into a language of its own.

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build
dotnet test
```

Run a program directly with the interpreter:

```sh
dotnet run --project src/Caca.Cli -- run samples/helloworld.caca
```

Or compile it to a real .NET assembly and run that:

```sh
dotnet run --project src/Caca.Cli -- build samples/helloworld.caca -o helloworld.dll
dotnet helloworld.dll
```

### Command line

```
caca repl                            Start an interactive prompt
caca run <file.caca>                 Run a program with the interpreter
caca build <file.caca> [-o <path>]   Compile a program to a .NET assembly
caca check <file.caca>               Report errors without running anything
caca <file.caca>                     Shorthand for 'caca build'
```

`build` writes the assembly together with the `.runtimeconfig.json` the .NET
host needs to launch it.

## Language

```
func isPrime(n: int): bool do
    if n < 2 then
        return false;
    end;

    var factor = 2;
    while factor * factor <= n do
        if n % factor == 0 then
            return false;
        end;
        factor = factor + 1;
    end;

    return true;
end

for n = 2 to 30 do
    if isPrime(n) then
        print n + " is prime";
    end;
end;
```

The full specification, including the semantics of each construct, is in
[`docs/grammar.txt`](docs/grammar.txt). In brief:

| | |
|---|---|
| Types | `int` (32-bit signed), `string`, `bool`; written as `var x: int = 1` or inferred |
| Functions | `func f(a: int): int do … end`, called from anywhere in the file, recursive and mutually recursive |
| Statements | `var`, assignment, `if … then … else … end`, `for … to … do … end`, `while … do … end`, `break`, `continue`, `return`, `read_int`, `read_string`, `print` |
| Arithmetic | `+ - * / %` with the usual precedence, left associative; unary `-` |
| Comparison | `< <= > >= == !=`; `==` compares strings by content |
| Logic | `&& \|\| !`, short-circuiting; conditions must be `bool`, with no integer truthiness |
| Strings | `+` concatenates; `\n \r \t \0 \\ \"` escapes |
| Comments | `// line` and `/* block */` |

## Tooling

### An interactive prompt

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

A bare expression is printed; anything else runs as a statement. Entries carry
over, and a definition spanning several lines is read until it is complete.
`:list`, `:reset` and `:quit` do what they sound like.

### Editor support

`caca-langserver` implements the Language Server Protocol, and
[`editors/vscode`](editors/vscode) is a Visual Studio Code extension that uses
it.

| Feature | Request |
|---|---|
| Errors as you type | `textDocument/publishDiagnostics` |
| Hover showing a name's type | `textDocument/hover` |
| Go to definition | `textDocument/definition` |
| Find all references | `textDocument/references` |
| Outline and breadcrumbs | `textDocument/documentSymbol` |

```sh
dotnet publish src/Caca.LanguageServer -c Release -o ~/.caca/bin
```

Then put it on your `PATH` and install the extension; see
[`editors/vscode/README.md`](editors/vscode/README.md).

The server compiles the whole file on every keystroke. Programs in this
language are small enough that this is imperceptible, and it means the editor
sees exactly what the compiler sees, with no second, incremental analysis that
could disagree with it. The protocol framing is written out in
[`Protocol/JsonRpc.cs`](src/Caca.LanguageServer/Protocol/JsonRpc.cs) rather
than taken from a package, because it is only a few dozen lines and this
project is meant to be read.

## How it works

| Stage | Where | What it does |
|---|---|---|
| Lexer | `src/Caca.Compiler/Syntax/Lexer.cs` | Source text to tokens carrying source positions |
| Parser | `src/Caca.Compiler/Syntax/Parser.cs` | Recursive descent, precedence climbing for expressions |
| Type checker | `src/Caca.Compiler/Binding/TypeChecker.cs` | Collects function signatures, resolves the type of every expression, binds every name to a symbol, reports semantic errors |
| Language server | `src/Caca.LanguageServer/` | Answers an editor's questions from those symbols |
| Interpreter | `src/Caca.Compiler/Runtime/Interpreter.cs` | Walks the tree and executes it (`caca run`) |
| IL emitter | `src/Caca.Compiler/Emit/IlEmitter.cs` | Writes a .NET assembly with `PersistedAssemblyBuilder` (`caca build`) |

Errors are collected as diagnostics rather than thrown, so one run reports every
problem it can find:

```
$ caca check broken.caca
broken.caca(2,7): error CACA0010: operator '*' is not defined for types string and int
broken.caca(3,7): error CACA0008: 'y' is not declared; use 'var y = ...' before using it
2 errors.
```

The two backends are expected to agree on every program, and the test suite
asserts that directly.

## Where it came from

The original targeted .NET Framework 2.0 and could only be built with Visual
Studio on Windows.

**Portability.** The emitter used `AppDomain.DefineDynamicAssembly` and
`AssemblyBuilder.Save`, neither of which exists outside .NET Framework. It now
uses `PersistedAssemblyBuilder` (.NET 9+) and writes the PE file with
`ManagedPEBuilder`. The non-SDK project file, `AssemblyInfo.cs` and the
ClickOnce bootstrapper configuration are gone; there are now three SDK-style
projects and a CI workflow that builds and tests on all three operating
systems.

**Correctness.** These programs were all accepted by the original compiler and
all did the wrong thing:

| Program | Was | Now |
|---|---|---|
| `print 10 - 2 - 3;` | `11` | `5` |
| `print 10 / 2 * 5;` | `1` | `25` |
| `var x = 1; print x + 1;` | garbage — invalid IL | `2` |
| `print 5 / 0;` | `InvalidProgramException` | a division-by-zero error |
| `print "a" + "b";` | `NullReferenceException` | `ab` |
| `var x1 = 5;` | syntax error | works, per the grammar |
| `print 2` (no trailing `;`) | `IndexOutOfRangeException` | works |
| `for x = 1 to 3` | ran twice | runs three times |
| `var a = -5;` | syntax error | works |
| `for i = 1 to 3` | `undeclared variable 'i'` | the loop declares `i` |

**Diagnostics.** Every error now carries a file, line, column and a stable
`CACA0001`-style code, instead of a bare sentence delivered as an unhandled
exception with a .NET stack trace.

**Design.** The token stream was an `IList<object>` in which identifiers were
`string` and string literals were `StringBuilder` — that difference was the only
thing distinguishing them. Types were computed by overriding `object.GetType()`
on AST nodes, which forced the symbol table to be a public static field so those
nodes could reach into the code generator. Both are gone, in favour of a proper
token type and a separate type-checking pass.

## Credits

Original code copyright (c) Microsoft Corporation, by Joel Pobar and Joe Duffy.
The original terms were published at a page that no longer exists.
