using FC.Engine.Infrastructure.Validation;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Validation;

public class ExpressionParserTests
{
    private readonly ExpressionParser _parser = new();

    [Fact]
    public void Evaluate_SimpleEquality_Passing()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 100 };
        var result = _parser.Evaluate("A = B", vars);

        result.Passes.Should().BeTrue();
        result.LeftValue.Should().Be(100);
        result.RightValue.Should().Be(100);
    }

    [Fact]
    public void Evaluate_SimpleEquality_Failing()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 200 };
        var result = _parser.Evaluate("A = B", vars);

        result.Passes.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Addition()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 200, ["C"] = 300 };
        var result = _parser.Evaluate("A + B = C", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Subtraction()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 500, ["B"] = 200, ["C"] = 300 };
        var result = _parser.Evaluate("A - B = C", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Multiplication()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 1000, ["B"] = 0.125m, ["C"] = 125 };
        var result = _parser.Evaluate("A * B = C", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Division()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 4, ["C"] = 25 };
        var result = _parser.Evaluate("A / B = C", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_DivisionByZero_ShouldReturnZero()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 0 };
        var result = _parser.Evaluate("A / B", vars);

        result.LeftValue.Should().Be(0);
    }

    [Fact]
    public void Evaluate_GreaterThan()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 200, ["B"] = 100 };
        var result = _parser.Evaluate("A > B", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GreaterThanOrEqual()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 100 };
        var result = _parser.Evaluate("A >= B", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_LessThan()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 50, ["B"] = 100 };
        var result = _parser.Evaluate("A < B", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NotEqual()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 50, ["B"] = 100 };
        var result = _parser.Evaluate("A != B", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Parentheses_ShouldRespectPrecedence()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 10, ["B"] = 20, ["C"] = 3, ["D"] = 90 };
        var result = _parser.Evaluate("(A + B) * C = D", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_OperatorPrecedence_ShouldMultiplyBeforeAdd()
    {
        // A + B * C should be A + (B*C) = 10 + 60 = 70
        var vars = new Dictionary<string, decimal> { ["A"] = 10, ["B"] = 20, ["C"] = 3 };
        var result = _parser.Evaluate("A + B * C", vars);

        result.LeftValue.Should().Be(70);
    }

    [Fact]
    public void Evaluate_AbsFunction()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = -50, ["B"] = 50 };
        var result = _parser.Evaluate("ABS(A) = B", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ComplexFinancialFormula()
    {
        // Typical CBN formula: total_assets = cash + investments + loans
        var vars = new Dictionary<string, decimal>
        {
            ["cash"] = 1000000,
            ["investments"] = 2000000,
            ["loans"] = 3000000,
            ["total_assets"] = 6000000
        };
        var result = _parser.Evaluate("total_assets = cash + investments + loans", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoComparison_ShouldReturnArithmeticValue()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 10, ["B"] = 20 };
        var result = _parser.Evaluate("A + B", vars);

        result.Passes.Should().BeTrue();
        result.LeftValue.Should().Be(30);
        result.RightValue.Should().BeNull();
    }

    [Fact]
    public void Evaluate_UnknownVariable_ShouldDefaultToZero()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100 };
        var result = _parser.Evaluate("A + unknown_field", vars);

        result.LeftValue.Should().Be(100);
    }

    [Fact]
    public void Evaluate_NestedParentheses()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 2, ["B"] = 3, ["C"] = 4, ["D"] = 5 };
        // ((A + B) * (C + D)) = (5 * 9) = 45
        var result = _parser.Evaluate("(A + B) * (C + D)", vars);

        result.LeftValue.Should().Be(45);
    }

    [Fact]
    public void Evaluate_RatioComparison()
    {
        // CBN ratio: capital / assets >= 0.10
        var vars = new Dictionary<string, decimal>
        {
            ["capital"] = 500000,
            ["assets"] = 4000000
        };
        var result = _parser.Evaluate("capital / assets >= 0.125", vars);

        result.Passes.Should().BeTrue();
        result.LeftValue.Should().Be(0.125m);
    }
}
