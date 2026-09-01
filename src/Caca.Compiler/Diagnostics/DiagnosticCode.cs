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
    UnknownType = 14,
    FunctionAlreadyDeclared = 15,
    UndeclaredFunction = 16,
    WrongArgumentCount = 17,
    ReturnOutsideFunction = 18,
    NotAllPathsReturn = 19,
    NoValueProduced = 20,
    FloatOutOfRange = 21,
    ExternTargetInvalid = 22,
    ExternTargetNotFound = 23,
    ExternReturnTypeMismatch = 24,
}
