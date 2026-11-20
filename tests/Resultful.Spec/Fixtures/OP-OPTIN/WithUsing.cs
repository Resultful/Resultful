using Resultful;
using Resultful.Operators;

public static class WithUsing
{
    static Result<int> A(Result<int> a, Result<int> b) => a | b;
    static Result<int> NoPlus(Result<int> a, Result<int> b) => a + b; // expect: CS0019
    static Validation<int> NoPlusOnValidation(Validation<int> a, Validation<int> b) => a + b; // expect: CS0019
}
