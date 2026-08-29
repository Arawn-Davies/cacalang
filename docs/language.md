# The cacalang language

A tour of everything in the language. The formal grammar is in
[`grammar.txt`](grammar.txt); this is the readable version.

Programs are statements separated by `;`. The last one may leave it off.

```
print "hello, world";
```

## Types

There are four: `int`, `float`, `string` and `bool`.

| Type | What it is | Literals |
|---|---|---|
| `int` | 32-bit signed integer | `0`, `42` |
| `float` | 64-bit IEEE 754 | `1.5`, `3.14159` |
| `string` | text | `"hello"`, `"a\nb"` |
| `bool` | a truth value | `true`, `false` |

A float literal needs digits on both sides of its point: `1.0` is a float, `1`
is an int, and in `1.` the dot is not part of the number.

Strings understand `\n`, `\r`, `\t`, `\0`, `\\` and `\"`. Any other escape is
an error rather than a silently kept backslash.

## Variables

`var` declares one. The type comes from the value, or is written:

```
var count = 0;          // int
var ratio = 0.5;        // float
var name: string = "";  // written out
```

A variable must be declared before it is used, cannot be declared twice, and
cannot later hold a different type.

```
var x = 1;
x = 2;      // fine
x = "two";  // error CACA0009
```

There is one scope per function, and one for the top level. A variable declared
inside a loop or an `if` is still there afterwards.

## Arithmetic

`+ - * / %`, with `* / %` binding tighter than `+ -`, all left associative.

```
print 10 - 2 - 3;   // 5
print 10 / 2 * 5;   // 25
print 2 + 3 * 4;    // 14
print (2 + 3) * 4;  // 20
```

If both operands are ints the result is an int, so `7 / 2` is `3`. If either is
a float the other is widened, so `7 / 2.0` is `3.5`. An int becomes a float
wherever one is expected, because no int loses anything by becoming one; a float
never becomes an int, because that would lose something silently.

```
var half: float = 1;  // 1.0
print 1 + 1.5;        // 2.5
```

Dividing an int by zero is an error. Dividing a float by zero is not: IEEE 754
says the answer is an infinity, and `0.0 / 0.0` is a NaN, which compares false
against everything including itself.

Unary `-` negates. `+` also joins strings, converting the other operand:

```
print "count: " + 42;    // count: 42
print "ready: " + true;  // ready: true
```

## Comparison and logic

`== != < <= > >=` produce a `bool`. Ordering compares numbers; equality compares
any two values of one type, and compares strings by their contents.

`&&`, `||` and `!` work on `bool` only. `&&` and `||` evaluate their right
operand only when the left has not already settled the answer:

```
if count != 0 && total / count > 10 then ... end;
```

There is no integer truthiness. `if 1 then` is an error, not a silent success.

## if

```
if n < 0 then
    print "negative";
else if n == 0 then
    print "zero";
else
    print "positive";
end;
```

An `else if` chain closes with a single `end`.

## Loops

`for` counts between two ints, and **both bounds are inclusive**:

```
for i = 1 to 3 do
    print i;        // 1, 2, 3
end;
```

The upper bound is worked out once, before the first turn. If the variable does
not exist the loop declares it, and it is still in scope afterwards.

`while` repeats as long as a condition holds:

```
var n = 3;
while n > 0 do
    print n;
    n = n - 1;
end;
```

`break` leaves the innermost loop; `continue` starts its next turn. Either
outside a loop is an error.

## Functions

```
func add(a: int, b: int): int do
    return a + b;
end

print add(3, 4);
```

Parameter types are always written. A function with no written return type
returns nothing and can only be called as a statement:

```
func greet(name: string) do
    print "hello, " + name;
end

greet("world");
```

A function may be called from anywhere in the file, including above its own
declaration, so recursion and mutual recursion both work:

```
func factorial(n: int): int do
    if n <= 1 then return 1; end;
    return n * factorial(n - 1);
end
```

A function that owes a value must produce one on **every** path; falling off the
end is an error, not a silently returned zero.

Parameters are passed by value: assigning to one inside a function does not
touch the caller's variable. **There are no globals** — a function sees its own
parameters and locals and nothing else.

## Input and output

`print` writes a value and a newline. `read_int`, `read_float` and `read_string`
read one line into an already-declared variable of the matching type.

```
var n = 0;
print "How many?";
read_int n;
```

Numbers are read and written with the invariant culture, so a program behaves
the same way whatever the machine's regional settings. A float keeps a trailing
`.0` when it would otherwise look like an int, so `1.0` prints as `1.0`.

Input that is not a number is a runtime error rather than a crash.

## Comments

```
// to the end of the line

/* over
   several lines */
```

## What the language does not have

No arrays, no globals, no block scoping, no modules, no user-defined types, no
exceptions, and one numeric type of each kind. Arrays and `foreach` are next; see
the [README](../README.md).
