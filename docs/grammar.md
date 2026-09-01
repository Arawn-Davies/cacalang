# cacalang language specification

Written in a simple EBNF style, the metasyntax the original article used.

Constructs marked (new) were added by this project. The rest comes from the
Good for Nothing language as published, or from the repository this was forked
from, which had already added `read_string`. Where the original definition was
ambiguous — it did not say how large a number could be, or whether one could be
negative — the ambiguity is resolved here in favour of what its own samples
imply. See [`history.md`](history.md).

## Grammar

```ebnf
<program>    := (<func_decl> | <stmt_list>)*                      ; functions (new)
<stmt_list>  := <stmt> (';' <stmt>)* ';'?      ; a trailing ';' is optional (new)

<func_decl>  := func <ident> '(' <params>? ')' (':' <type>)? do <stmt_list> end   ; (new)
              | extern func <ident> '(' <params>? ')' (':' <type>)? from <string>  ; (new)
<params>     := <param> (',' <param>)*                           ; (new)
<param>      := <ident> ':' <type>                               ; (new)
<type>       := int | float | string | bool                      ; (new)

<stmt> := var <ident> (':' <type>)? = <expr>            ; annotation (new)
        | <ident> = <expr>
        | for <ident> = <expr> to <expr> do <stmt_list> end
        | while <expr> do <stmt_list> end                        ; (new)
        | if <expr> then <stmt_list> <else_tail>? end            ; (new)
        | break                                                  ; (new)
        | continue                                               ; (new)
        | return <expr>?                                         ; (new)
        | <call>                                                 ; (new)
        | read_int <ident>
        | read_string <ident>
        | read_float <ident>                                     ; (new)
        | print <expr>

<else_tail> := else <stmt_list>                                  ; (new)
             | else if <expr> then <stmt_list> <else_tail>?      ; (new)

; Operators, loosest binding first. All binary operators are left associative.
<expr>           := <or>
<or>             := <and> ('||' <and>)*                          ; (new)
<and>            := <equality> ('&&' <equality>)*                ; (new)
<equality>       := <relational> (('==' | '!=') <relational>)*    ; (new)
<relational>     := <additive> (('<' | '<=' | '>' | '>=') <additive>)*  ; (new)
<additive>       := <multiplicative> (('+' | '-') <multiplicative>)*
<multiplicative> := <unary> (('*' | '/' | '%') <unary>)*         ; '%' (new)
<unary>          := ('+' | '-' | '!') <unary> | <primary>        ; unary operators (new)
<primary>        := <int> | <string> | <bool> | <ident> | '(' <expr> ')'

<bool>       := true | false                                     ; (new)

<ident>      := (<letter> | '_') (<letter> | <digit> | '_')*
<int>        := <digit>+
<float>      := <digit>+ '.' <digit>+                            ; (new)
<digit>      := 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9

<string>       := '"' <string_elem>* '"'
<string_elem>  := <any char other than '"', '\' or a newline> | <escape>
<escape>       := '\' ('n' | 'r' | 't' | '0' | '\' | '"')     ; escapes (new)

<comment>    := '//' <any char>* <newline>                     ; (new)
              | '/*' <any char>* '*/'                          ; (new)
```

## Semantics

### Types

There are four types: int (32-bit, signed), float, string and bool.

float is a 64-bit IEEE 754 binary floating point number. There is one
floating point type rather than a separate single and double precision pair,
because two of them buy a conversion matrix and very little else.

An int converts to a float wherever one is expected: in an operation mixing
the two, in an assignment, in an argument and in a return. Every int is
exactly representable as a float, so nothing is lost. Nothing converts the
other way, because that would lose information silently.

A float literal needs digits on both sides of its point, so `1.0` is a float
and `1` is an int. In `1.` the dot is not part of the number.

### Operators

'+' '-' '*' '/' '%' apply to two numbers. If both are ints the result is an
int; if either is a float, the other is widened and the result is a float.
So `7 / 2` is 3 and `7 / 2.0` is 3.5. Multiplication, division and remainder
bind tighter than addition and subtraction, and all are left associative, so
`10 - 2 - 3` is 5 and `10 / 2 * 5` is 25.
'+' also concatenates when either operand is a string; an int or bool operand
is converted, ints using the invariant culture and bools as "true"/"false".
No other arithmetic operator accepts a string. Unary '-' negates a number.

Integer division and remainder by zero are runtime errors. Float division by
zero is not: IEEE 754 defines the answer, and it is an infinity or, for 0/0,
a NaN. A NaN compares false against everything, itself included.

'<' '<=' '>' '>=' compare two numbers and produce a bool.
'==' and '!=' compare two values of the same type, including strings, which
are compared by their contents rather than by reference.
'&&' and '||' take two bools and evaluate their right operand only when the
left one has not already decided the result. Unary '!' negates a bool.

### if / while

Conditions must be of type bool: there is no integer truthiness, so
`if 1 then` is an error rather than a silent success. An `else if` chain is
closed by a single `end`.

### break / continue

Leave, or restart, the innermost enclosing `for` or `while`. Using either
outside a loop is an error.

### Variables

Every variable must be declared with `var` before use. Its type is written,
as in `var x: int = 1`, or inferred from the initializer. A variable cannot
be redeclared, and cannot be assigned a value of a different type.

### Functions

A function may be called from anywhere in the file, including from above its
declaration and from itself, so recursion and mutual recursion both work.
Parameter types are always written; a function with no written return type
returns nothing and may only be called as a statement. A function that does
return a value must do so on every path.

Parameters are passed by value: assigning to one inside a function does not
affect the caller's variable.

### Extern functions

`extern func` binds a name to a .NET method instead of a body. The target
string is the method's namespace-qualified type followed by the method name,
as in `"System.Math.Sqrt"`. The declared signature selects the overload: the
compiler resolves a public static method whose parameter types are exactly
the declared ones (`int` is `Int32`, `float` is `Double`, `string` is
`String`, `bool` is `Boolean`), whose return type is the declared one, or
`void` for a declaration with no return type. When no static method matches,
an instance method is resolved with the first declared parameter as its
receiver; the receiver must be a reference type. A property is reached
through its getter, as in `"System.String.get_Length"`.

The target type is looked up in the referenced assemblies given to the
compiler, then the core library, then the .NET runtime's own assemblies —
deliberately not everything loaded into the compiling process, whose contents
a compiled program cannot rely on. Parameter types must match exactly:
reflection's implicit widenings are not used, so an `int` declaration never
binds to a `double` parameter. A target that does not resolve — a malformed
name, a missing type or method, a signature or return type that matches
nothing — is a compile-time error.

`extern` is a keyword; `from` is contextual, recognized only in this
declaration, so existing programs using `from` as a name are unaffected.

A call to an extern function follows every rule a call to a declared function
follows: arguments are type-checked, ints widen to float parameters, and a
void function is only a statement. An exception thrown by the bound method is
a runtime error naming the function. A null returned by a .NET method — the
language itself has none — mostly behaves as an empty string, but is not
equal to `""`.

### Scope

There are no globals. A function sees its own parameters and the variables it
declares, and nothing else; the top-level statements likewise have a scope of
their own. Within one of those scopes there is a single flat namespace, so a
variable declared inside a loop or an `if` remains visible after it.

### for

Both bounds are ints and both are inclusive, so `for i = 1 to 3` runs three
times. The upper bound is evaluated once, before the first iteration. If the
loop variable has not been declared, the loop declares it as an int; it
remains visible after the loop ends. The body may assign to the loop
variable, which affects the following iteration.

### read_int / read_float / read_string

Reads one line from standard input into an already-declared variable of the
matching type. Input that is not a number is a runtime error. Numbers are
read, and written, using the invariant culture, so a program behaves the same
way regardless of the machine's regional settings.

### print

A float is written in the shortest form that reads back as the same value,
with a trailing ".0" when that form has neither a point nor an exponent, so
that a float never looks like an int. Infinities and NaN are written as
"Infinity", "-Infinity" and "NaN".
