# Diagnostics

Every error the compiler reports carries a file, a line, a column and a stable
code, so it can be found by eye and matched by a tool:

```
loop.caca(2,7): error CACA0010: operator '*' is not defined for types string and int
```

A run reports every error it can find rather than stopping at the first, and the
parser recovers at statement boundaries, so one mistake does not hide the rest
of the file.

## Lexical

These come from reading the characters of the file.

| Code | Meaning |
|---|---|
| `CACA0001` | An unexpected character. A lone `&` or `\|` suggests the pair, since that is nearly always the intent. |
| `CACA0002` | An unterminated string literal. A string may not span a line. |
| `CACA0003` | An unrecognized escape sequence. The escapes are `\n`, `\r`, `\t`, `\0`, `\\` and `\"`. |
| `CACA0004` | An integer literal outside the range of `int`. |
| `CACA0021` | A floating point literal outside the range of `float`. |

## Syntactic

These come from the shape of the program.

| Code | Meaning |
|---|---|
| `CACA0005` | An unexpected token: something else was expected here. The message names both. |
| `CACA0006` | An expression was expected. |

## Semantic

These come from what the program means.

| Code | Meaning |
|---|---|
| `CACA0007` | A variable, or a parameter, is declared twice in one scope. |
| `CACA0008` | A name is used that was never declared. |
| `CACA0009` | A type mismatch: an assignment, an argument, a `return`, a `read_*` target, or a condition that is not `bool`. |
| `CACA0010` | An operator is not defined for the operand types, such as `"a" * 2`. |
| `CACA0011` | A `for` loop bound is not an `int`. |
| `CACA0012` | A `for` loop variable exists already and is not an `int`. |
| `CACA0013` | `break` or `continue` outside any loop. |
| `CACA0014` | A written type that is not one of `int`, `float`, `string`, `bool`. |
| `CACA0015` | Two functions with one name. |
| `CACA0016` | A call to a function that is not declared. |
| `CACA0017` | A call with the wrong number of arguments. The message shows the signature. |
| `CACA0018` | `return` outside any function. |
| `CACA0019` | A function that owes a value can reach its end without returning one. |
| `CACA0020` | A value was needed where the expression produces none, such as printing the result of a function that returns nothing. |
| `CACA0022` | An extern target that does not name a method: it must be `"Namespace.Type.Method"`. |
| `CACA0023` | An extern target that does not resolve: the type was not found, or it has no public method with that name and the declared parameter types. |
| `CACA0024` | An extern target whose method returns a different type than the declaration. |
| `CACA0025` | An extern function in a program compiled with `--target c`, which has no .NET to call. |

`CACA0019` is decided by asking whether a statement returns on every path: a
`return` does, a block does if any statement in it does, and an `if` does if it
has an `else` and both branches do. Loops are deliberately not counted, because
proving that a loop always runs at least once is more analysis than this needs.

## Runtime errors

These are not diagnostics: they happen while a program runs, and are reported as
a single line rather than a stack trace.

| Message | Cause |
|---|---|
| `attempted to divide by zero` | Integer `/` or `%` by zero. Float division by zero is **not** an error: IEEE 754 gives an infinity, or a NaN for `0.0 / 0.0`. |
| `'…' is not an integer` | `read_int` given input that is not one. |
| `'…' is not a number` | `read_float` given input that is not one. |
| `call stack depth of … exceeded` | Runaway recursion. The interpreter stops rather than overflowing the stack, which cannot be caught and would take the process down. |
| `'…' failed: …` | An extern function's .NET method threw; the message is the exception's. |
| `'…' was called on a null …` | An extern instance method's receiver was a null returned by another .NET method. |

## Where the codes live

`src/Caca.Compiler/Diagnostics/DiagnosticCode.cs`. The numbers are stable:
add new ones at the end rather than renumbering, because they appear in editor
output, in CI logs and in this file.
