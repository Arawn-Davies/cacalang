namespace Caca.Diagnostics;

/// <summary>Stable identifiers for every error the compiler can report.</summary>
public enum DiagnosticCode
{
    None = 0,

    // Lexical
    UnexpectedCharacter = 1,
    UnterminatedString = 2,
    InvalidEscapeSequence = 3,
    IntegerOutOfRange = 4,

    // Syntactic
    UnexpectedToken = 5,
    ExpectedExpression = 6,

    // Semantic
    VariableAlreadyDeclared = 7,
    UndeclaredVariable = 8,
    TypeMismatch = 9,
    OperatorNotDefined = 10,
    LoopBoundMustBeInt = 11,
    LoopVariableMustBeInt = 12,
    NotInsideALoop = 13,
}
