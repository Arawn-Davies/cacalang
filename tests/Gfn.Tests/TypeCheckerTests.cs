using Gfn.Diagnostics;

namespace Gfn.Tests;

public class TypeCheckerTests
{
    [Fact]
    public void Undeclared_variable_is_reported()
    {
        var error = TestHost.SingleError("print x;", DiagnosticCode.UndeclaredVariable);
        Assert.Contains("'x' is not declared", error.Message);
    }

    [Fact]
    public void Redeclaring_a_variable_is_reported()
    {
        TestHost.SingleError("var x = 1; var x = 2;", DiagnosticCode.VariableAlreadyDeclared);
    }

    [Fact]
    public void Assigning_the_wrong_type_is_reported()
    {
        TestHost.SingleError("""var x = 1; x = "hello";""", DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Reading_an_int_into_a_string_variable_is_reported()
    {
        TestHost.SingleError("""var s = "hi"; read_int s;""", DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData(@"var s = ""a""; print s - ""b"";")]
    [InlineData(@"var s = ""a""; print s * 2;")]
    [InlineData(@"var s = ""a""; print s / 2;")]
    [InlineData(@"var s = ""a""; print -s;")]
    public void Arithmetic_on_strings_is_rejected(string source)
    {
        // The original compiler emitted an integer `add` over string references
        // for these, producing IL the runtime rejects.
        TestHost.SingleError(source, DiagnosticCode.OperatorNotDefined);
    }

    [Fact]
    public void String_loop_bounds_are_rejected()
    {
        var errors = TestHost.Errors("""var s = "a"; for i = 1 to s do print i; end;""");
        Assert.Contains(errors, e => e.Code == DiagnosticCode.LoopBoundMustBeInt);
    }

    [Fact]
    public void A_string_loop_variable_is_rejected()
    {
        var errors = TestHost.Errors("""var s = "a"; for s = 1 to 3 do print s; end;""");
        Assert.Contains(errors, e => e.Code == DiagnosticCode.LoopVariableMustBeInt);
    }

    [Fact]
    public void One_undeclared_variable_produces_one_error_per_use_site_only()
    {
        // A failed declaration still binds the name, so later uses stay quiet.
        var errors = TestHost.Errors("var x = y; print x; print x;");
        Assert.Single(errors);
    }
}
