# The original sketch

The informal grammar that came with the Good for Nothing compiler in 2005,
kept verbatim for reference. It describes the language as it was then, not as
it is now: see [`grammar.md`](grammar.md) for the current specification and
[`history.md`](history.md) for what changed.

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
