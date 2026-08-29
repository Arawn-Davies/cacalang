# Where cacalang came from

cacalang began as the Good for Nothing compiler that Joel Pobar and Joe Duffy
presented at PDC 2005, published with
[an article in MSDN Magazine](https://msdn.microsoft.com/en-us/magazine/cc136756.aspx).
It compiled a tiny language — variables, `print`, `read_int` and a counted loop —
to .NET IL, as a way of showing how a compiler is put together.

That compiler targeted .NET Framework 2.0 and could only be built with Visual
Studio on Windows. This is what changed.

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

Original code copyright (c) Microsoft Corporation, by Joel Pobar and Joe Duffy.
The original terms were published at a page that no longer exists.
