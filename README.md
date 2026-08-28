# Good for Nothing Compiler

A small compiler for "Good for Nothing", a C-like toy language with variables,
console I/O, arithmetic and a `for` loop.

This is a continuation of the Good for Nothing compiler presented by Joel Pobar
and Joe Duffy at PDC 2005, published with
[an article in MSDN Magazine](https://msdn.microsoft.com/en-us/magazine/cc136756.aspx).
It has since been modernised: it builds and runs on Windows, macOS and Linux
with the .NET 10 SDK, has a real type checker, reports errors with source
positions, and ships with two backends and a test suite.

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build
dotnet test
```

Run a program directly with the interpreter:

```sh
dotnet run --project src/Gfn.Cli -- run samples/helloworld.gfn
```

Or compile it to a real .NET assembly and run that:

```sh
dotnet run --project src/Gfn.Cli -- build samples/helloworld.gfn -o helloworld.dll
dotnet helloworld.dll
```

### Command line

```
gfn run <file.gfn>                 Run a program with the interpreter
gfn build <file.gfn> [-o <path>]   Compile a program to a .NET assembly
gfn check <file.gfn>               Report errors without running anything
gfn <file.gfn>                     Shorthand for 'gfn build'
```

`build` writes the assembly together with the `.runtimeconfig.json` the .NET
host needs to launch it.

## Language

```
var x = 2;
var y = 4;
var z = y / x;
print z;
print "that's it folks!";
```

```
var ntimes = 0;
print "How much do you love this company? (1-10) ";
read_int ntimes;
for x = 1 to ntimes do
    print "Developers!";
end;
print "Who said sit down?!!!!!";
```

The full specification, including the semantics of each construct, is in
[`docs/grammar.txt`](docs/grammar.txt). In brief:

| | |
|---|---|
| Types | `int` (32-bit signed) and `string` |
| Statements | `var`, assignment, `for … to … do … end`, `read_int`, `read_string`, `print` |
| Operators | `+ - * /` with the usual precedence, left associative; unary `-`; parentheses |
| Strings | `+` concatenates; `\n \r \t \0 \\ \"` escapes |
| Comments | `// line` and `/* block */` |

## How it works

| Stage | Where | What it does |
|---|---|---|
| Lexer | `src/Gfn.Compiler/Syntax/Lexer.cs` | Source text to tokens carrying source positions |
| Parser | `src/Gfn.Compiler/Syntax/Parser.cs` | Recursive descent, precedence climbing for expressions |
| Type checker | `src/Gfn.Compiler/Binding/TypeChecker.cs` | Resolves the type of every expression and reports semantic errors |
| Interpreter | `src/Gfn.Compiler/Runtime/Interpreter.cs` | Walks the tree and executes it (`gfn run`) |
| IL emitter | `src/Gfn.Compiler/Emit/IlEmitter.cs` | Writes a .NET assembly with `PersistedAssemblyBuilder` (`gfn build`) |

Errors are collected as diagnostics rather than thrown, so one run reports every
problem it can find:

```
$ gfn check broken.gfn
broken.gfn(2,7): error GFN0010: operator '*' is not defined for types string and int
broken.gfn(3,7): error GFN0008: 'y' is not declared; use 'var y = ...' before using it
2 errors.
```

The two backends are expected to agree on every program, and the test suite
asserts that directly.

## What changed from the original

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
`GFN0001`-style code, instead of a bare sentence delivered as an unhandled
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
