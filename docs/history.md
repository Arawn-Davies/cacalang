# Where cacalang came from

cacalang began as the Good for Nothing compiler, written by Joel Pobar and
published in MSDN Magazine in February 2008 as
["Create a Language Compiler for the .NET Framework"](https://learn.microsoft.com/en-us/archive/msdn-magazine/2008/february/create-a-language-compiler-for-the-net-framework-using-csharp).
It compiled a tiny language — variables, `print`, `read_int` and a counted loop —
to .NET IL, as a way of showing how a compiler is put together.

That compiler targeted .NET Framework 2.0 and could only be built with Visual
Studio on Windows. This is what changed.

## What the original was

The article walks through a compiler in three phases — a scanner, a parser and
a code generator — and is explicit that it is a teaching example rather than a
production one. Several things this project later changed are called out in it
as simplifications made on purpose:

- The scanner produced an `IList<object>`. On typing tokens as `object`, the
  article notes: *"I could have created a Token class or something similar to
  encapsulate more information about the token, such as line and column
  numbers."*
- The language defined two types, mapped to `System.Int32` and `System.String`.
- The parser is an LL top-down parser, and the grammar is left deliberately
  imprecise: *"I haven't specified how big the number can be ... or even if the
  number can be negative."*

It also warns about the exact class of bug the compiler shipped with:
*"Even the most seasoned compiler hackers make mistakes at the code-generation
level. The most common bug is bad IL code, which causes unbalance in the
stack."* Four of the ten programs in the table below failed that way. The
article recommends `peverify` for finding it; this project uses a test suite
that loads and runs every emitted assembly, which fails on the same thing.

## It would not build at all

The emitter used `AppDomain.DefineDynamicAssembly` and `AssemblyBuilder.Save`,
neither of which exists outside .NET Framework. Compiling the original sources
against `net10.0` produced exactly four errors, all in that one path.

It now uses `PersistedAssemblyBuilder` (.NET 9 and later) and writes the PE file
with `ManagedPEBuilder`. The non-SDK project file, `AssemblyInfo.cs` and the
ClickOnce bootstrapper configuration are gone, replaced by SDK-style projects
and a CI workflow that builds and tests on Windows, macOS and Linux.

## It compiled programs that did the wrong thing

Every one of these was accepted by the original compiler, and each now has a
regression test:

| Program | Was | Now |
|---|---|---|
| `print 10 - 2 - 3;` | `11` | `5` |
| `print 10 / 2 * 5;` | `1` | `25` |
| `var x = 1; print x + 1;` | garbage — invalid IL | `2` |
| `print 5 / 0;` | `InvalidProgramException` | a division-by-zero error |
| `print "a" + "b";` | `NullReferenceException` | `ab` |
| `var x1 = 5;` | syntax error | works, as the grammar always said |
| `print 2` (no trailing `;`) | `IndexOutOfRangeException` | works |
| `for x = 1 to 3` | ran twice | runs three times |
| `var a = -5;` | syntax error | works |
| `for i = 1 to 3` | `undeclared variable 'i'` | the loop declares `i` |

The first five share a cause. The emitter passed an expression's *expected* type
down to its operands, and arithmetic assumed the type of its left operand, so
the compiler would happily emit memory-unsafe IL. There is now a type-checking
pass between parsing and code generation.

The loop counting one step short was a `TODO` the original left in its parser.

## Errors were unhandled exceptions

Every problem was `throw new Exception("...")` with no position, surfacing to
whoever ran the compiler as a .NET stack trace. Errors are now diagnostics with
a file, a line, a column and a stable code, collected rather than thrown, so one
run reports everything it can find. See [`diagnostics.md`](diagnostics.md).

## Two designs that had to go

The token stream was an `IList<object>` in which identifiers were `string` and
string literals were `StringBuilder` — that difference was the only thing
distinguishing them.

Types were computed by overriding `object.GetType()` on the syntax nodes, which
shadowed a member every .NET type has, and forced the symbol table to be a
`public static` field so those nodes could reach into the code generator.

Both are gone: a real token type carrying positions, and a separate type checker
that records what it works out.

## What the language gained

The original could not express a decision: no `bool`, no comparison operators,
no `if`. Nothing beyond straight-line code with a counted loop could be written
in it.

Since then: `bool`, `float`, comparison and logical operators, `if`/`else`,
`while`, `break`, `continue`, functions with recursion, type annotations,
parentheses, unary operators, comments, string escapes and string
concatenation. [`language.md`](language.md) covers all of it.

And around it: an interpreter alongside the IL emitter, a language server, a
VS Code extension, a REPL, debugging symbols, and a native launcher so a
compiled program can be run directly.

## Credit

Original code copyright (c) Microsoft Corporation, by Joel Pobar, published with
["Create a Language Compiler for the .NET Framework"](https://learn.microsoft.com/en-us/archive/msdn-magazine/2008/february/create-a-language-compiler-for-the-net-framework-using-csharp),
MSDN Magazine, February 2008. The original terms were published at a page that
no longer exists.

The repository this was forked from credits the compiler to a Professional
Developers Conference presentation in 2005 by Joel Pobar and Joe Duffy. The
article itself carries only Joel Pobar's byline, and neither claim is supported
by it, so this project credits what the article states.
