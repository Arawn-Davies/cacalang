# Contributing

## Building

```sh
dotnet build
dotnet test
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Warnings are
errors.

## Adding a language feature

A feature is not done until it exists in both backends. The usual path, in
order:

1. **A token**, if the feature needs new syntax — `Syntax/TokenKind.cs` and
   `Syntax/Lexer.cs`.
2. **A node** in `Syntax/Ast.cs`, and the parsing for it in `Syntax/Parser.cs`.
   Give the node a location that covers the whole construct.
3. **Type rules** in `Binding/TypeChecker.cs`, and a new
   `Diagnostics/DiagnosticCode.cs` entry for anything it can reject. Add codes
   at the end; they appear in editor output and in `docs/diagnostics.md`.
4. **The interpreter**, in `Runtime/Interpreter.cs`.
5. **The emitter**, in `Emit/IlEmitter.cs` — including a sequence point if the
   feature is a statement a debugger should stop on.
6. **Tests**: the behaviour through the interpreter, the errors it can produce,
   and the same programs through the emitter.
7. **Documentation**: [`docs/grammar.md`](docs/grammar.md) and
   [`docs/language.md`](docs/language.md), plus
   [`docs/diagnostics.md`](docs/diagnostics.md) for any new code.

### The parity rule

The interpreter and the emitted assembly must produce identical output for every
program. `EmitterTests.Both_backends_produce_the_same_output` is where that is
asserted; add cases to it.

If a rule cannot be expressed in emitted IL — formatting a float, for instance —
generate a method into the assembly rather than letting the two drift apart.

### Errors are diagnostics

Report through the `DiagnosticBag` with a location. Do not throw: an editor
compiles a file on every keystroke, and most of those do not parse.

An exception is still right for a bug in the compiler itself, where the message
is for whoever is working on it rather than for whoever is writing a program.

## Tests

The suite runs in about half a second, so run it constantly.

- Test through observable behaviour — what a program prints — rather than
  through the shape of the tree.
- The emitter tests load the assembly and run it, because that is what proves
  the IL is valid: the runtime rejects a malformed method body when it jits it.
- Anything that redirects `Console` belongs in the `console` collection. xUnit
  runs test classes in parallel and those streams are process-wide.

## Style

Match the file you are in. Beyond that: comments explain *why*, names are
spelled out, and public members carry a `<summary>`. Where something looks odd
on purpose, say what it would otherwise do — those comments are the ones worth
having.
