# The original sketch

An informal sketch of the grammar, carried over verbatim from the repository
this project was forked from. It describes the language as it was then, not as
it is now: see [`grammar.md`](grammar.md) for the current specification and
[`history.md`](history.md) for what changed.

It already differs from the language as published, which had no `read_string`.

```
stmt =
	for ident = expr to expr do stmt end
	stmt; stmt
	var ident = expr
	ident = expr
	read_int ident
	read_string ident
	print expr

expr =
	string literal
	arithmetic expression
	ident
	
demo progs:

print "hello, world"
```
