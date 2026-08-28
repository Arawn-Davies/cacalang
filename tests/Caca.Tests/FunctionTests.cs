using Caca.Diagnostics;
using Caca.Runtime;

namespace Caca.Tests;

public class FunctionTests
{
    [Fact]
    public void A_function_returns_a_value()
    {
        Assert.Equal(TestHost.Lines("7"), TestHost.Run("""
            func add(a: int, b: int): int do
                return a + b;
            end

            print add(3, 4);
            """));
    }

    [Fact]
    public void A_function_with_no_return_type_is_called_as_a_statement()
    {
        Assert.Equal(TestHost.Lines("hello, world"), TestHost.Run("""
            func greet(name: string) do
                print "hello, " + name;
            end

            greet("world");
            """));
    }

    [Fact]
    public void A_function_takes_no_arguments()
    {
        Assert.Equal(TestHost.Lines("42"), TestHost.Run("""
            func answer(): int do
                return 42;
            end

            print answer();
            """));
    }

    [Fact]
    public void A_function_may_be_called_before_it_is_declared()
    {
        Assert.Equal(TestHost.Lines("4"), TestHost.Run("""
            print double(2);

            func double(n: int): int do
                return n * 2;
            end
            """));
    }

    [Fact]
    public void Functions_recurse()
    {
        Assert.Equal(TestHost.Lines("120"), TestHost.Run("""
            func factorial(n: int): int do
                if n <= 1 then
                    return 1;
                end;
                return n * factorial(n - 1);
            end

            print factorial(5);
            """));
    }

    [Fact]
    public void Functions_recurse_mutually()
    {
        Assert.Equal(TestHost.Lines("true", "false"), TestHost.Run("""
            func isEven(n: int): bool do
                if n == 0 then return true; end;
                return isOdd(n - 1);
            end

            func isOdd(n: int): bool do
                if n == 0 then return false; end;
                return isEven(n - 1);
            end

            print isEven(10);
            print isEven(7);
            """));
    }

    [Fact]
    public void A_bare_return_leaves_a_function_early()
    {
        Assert.Equal(TestHost.Lines("small"), TestHost.Run("""
            func describe(n: int) do
                if n < 10 then
                    print "small";
                    return;
                end;
                print "large";
            end

            describe(3);
            """));
    }

    [Fact]
    public void Return_leaves_a_function_from_inside_a_loop()
    {
        Assert.Equal(TestHost.Lines("3"), TestHost.Run("""
            func firstMultipleOfThree(limit: int): int do
                for i = 1 to limit do
                    if i % 3 == 0 then
                        return i;
                    end;
                end;
                return 0;
            end

            print firstMultipleOfThree(10);
            """));
    }

    [Fact]
    public void Return_leaves_a_function_from_inside_a_while_loop()
    {
        Assert.Equal(TestHost.Lines("8"), TestHost.Run("""
            func firstPowerOfTwoAbove(n: int): int do
                var p = 1;
                while true do
                    p = p * 2;
                    if p > n then return p; end;
                end;
                return 0;
            end

            print firstPowerOfTwoAbove(5);
            """));
    }

    [Fact]
    public void Parameters_are_local_to_the_call()
    {
        Assert.Equal(TestHost.Lines("1", "2"), TestHost.Run("""
            func shadow(n: int): int do
                n = n * 10;
                return n;
            end

            var n = 1;
            print n;
            n = 2;
            print n;
            """));
    }

    [Fact]
    public void Assigning_to_a_parameter_does_not_affect_the_caller()
    {
        Assert.Equal(TestHost.Lines("20", "2"), TestHost.Run("""
            func tenTimes(n: int): int do
                n = n * 10;
                return n;
            end

            var x = 2;
            print tenTimes(x);
            print x;
            """));
    }

    [Fact]
    public void A_function_cannot_see_top_level_variables()
    {
        // There are no globals: a function sees only its parameters and locals.
        // Both the read and the assignment target are undeclared inside `add`.
        var errors = TestHost.Errors("""
            var total = 0;

            func add(n: int) do
                total = total + n;
            end
            """);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(DiagnosticCode.UndeclaredVariable, e.Code));
    }

    [Fact]
    public void Variables_may_carry_a_written_type()
    {
        Assert.Equal(TestHost.Lines("3"), TestHost.Run("var x: int = 3; print x;"));
    }

    [Fact]
    public void A_written_type_that_contradicts_the_value_is_rejected()
    {
        TestHost.SingleError("""var x: int = "three";""", DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void An_unknown_type_name_is_rejected()
    {
        var error = TestHost.SingleError("var x: number = 3;", DiagnosticCode.UnknownType);
        Assert.Contains("int, string and bool", error.Message);
    }

    [Fact]
    public void Calling_an_undeclared_function_is_rejected()
    {
        TestHost.SingleError("print missing(1);", DiagnosticCode.UndeclaredFunction);
    }

    [Fact]
    public void The_wrong_number_of_arguments_is_rejected()
    {
        var error = TestHost.SingleError("""
            func add(a: int, b: int): int do return a + b; end
            print add(1);
            """, DiagnosticCode.WrongArgumentCount);

        Assert.Contains("add(a: int, b: int): int", error.Message);
    }

    [Fact]
    public void An_argument_of_the_wrong_type_is_rejected()
    {
        TestHost.SingleError("""
            func double(n: int): int do return n * 2; end
            print double("two");
            """, DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Returning_the_wrong_type_is_rejected()
    {
        TestHost.SingleError("""
            func f(): int do return "no"; end
            print f();
            """, DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void A_function_that_owes_a_value_must_return_on_every_path()
    {
        var error = TestHost.SingleError("""
            func f(n: int): int do
                if n > 0 then return 1; end;
            end
            print f(1);
            """, DiagnosticCode.NotAllPathsReturn);

        Assert.Contains("every path", error.Message);
    }

    [Fact]
    public void Returning_on_both_arms_of_an_if_satisfies_the_check()
    {
        Assert.Equal(TestHost.Lines("1"), TestHost.Run("""
            func sign(n: int): int do
                if n > 0 then return 1; else return 0; end;
            end
            print sign(5);
            """));
    }

    [Fact]
    public void Returning_a_value_from_a_function_that_returns_nothing_is_rejected()
    {
        TestHost.SingleError("""
            func f() do return 1; end
            f();
            """, DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Return_outside_a_function_is_rejected()
    {
        TestHost.SingleError("return 1;", DiagnosticCode.ReturnOutsideFunction);
    }

    [Fact]
    public void Using_the_result_of_a_function_that_returns_nothing_is_rejected()
    {
        TestHost.SingleError("""
            func f() do print 1; end
            print f();
            """, DiagnosticCode.NoValueProduced);
    }

    [Fact]
    public void Declaring_two_functions_with_one_name_is_rejected()
    {
        TestHost.SingleError("""
            func f(): int do return 1; end
            func f(): int do return 2; end
            print f();
            """, DiagnosticCode.FunctionAlreadyDeclared);
    }

    [Fact]
    public void Two_parameters_with_one_name_are_rejected()
    {
        TestHost.SingleError(
            "func f(a: int, a: int): int do return a; end f(1, 2);",
            DiagnosticCode.VariableAlreadyDeclared);
    }

    [Fact]
    public void Runaway_recursion_reports_an_error_instead_of_crashing_the_process()
    {
        var compilation = Compilation.Create("""
            func forever(n: int): int do return forever(n + 1); end
            print forever(1);
            """);

        var exception = Assert.Throws<CacaRuntimeException>(
            () => compilation.Run(new StringReader(string.Empty), new StringWriter()));

        Assert.Contains("call stack depth", exception.Message);
    }

    [Fact]
    public void Two_functions_may_use_the_same_local_names()
    {
        Assert.Equal(TestHost.Lines("10", "200"), TestHost.Run("""
            func a(): int do var x = 10; return x; end
            func b(): int do var x = 200; return x; end
            print a();
            print b();
            """));
    }
}
