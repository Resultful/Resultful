// RF0006 (16b): a result field no constructor assigns, so it stays the zero value (decision 10 layer 3).
// spec: uses analyzers
using Resultful;
using Resultful.CodeAnalysis;

public sealed class Order
{
    private readonly Result<int> _total; // expect: RF0006
    private readonly Result<int> _tax;
    public Order(int tax) => _tax = Result.Ok(tax);
    public int Total => _total.ValueOr(0) + _tax.ValueOr(0);
}
