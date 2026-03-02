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
        var vars = new Dictionary<string, decimal>
        {
            ["total"] = 300,
            ["cash"] = 100,
            ["investments"] = 200
        };
        var result = _parser.Evaluate("total = cash + investments", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Subtraction()
    {
        var vars = new Dictionary<string, decimal>
        {
            ["net"] = 50,
            ["gross"] = 100,
            ["deductions"] = 50
        };
        var result = _parser.Evaluate("net = gross - deductions", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Multiplication()
    {
        var vars = new Dictionary<string, decimal>
        {
            ["result"] = 25,
            ["A"] = 200,
        };
        var result = _parser.Evaluate("result = A * 0.125", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Division()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100, ["B"] = 4 };
        var result = _parser.Evaluate("A / B", vars);

        result.Passes.Should().BeTrue();
        result.LeftValue.Should().Be(25);
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
    public void Evaluate_Parentheses_ShouldRespectOrder()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 2, ["B"] = 3, ["C"] = 4 };
        // (2 + 3) * 4 = 20
        var result = _parser.Evaluate("(A + B) * C", vars);

        result.LeftValue.Should().Be(20);
    }

    [Fact]
    public void Evaluate_OperatorPrecedence_MultiplicationBeforeAddition()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 2, ["B"] = 3, ["C"] = 4 };
        // 2 + 3 * 4 = 14 (not 20)
        var result = _parser.Evaluate("A + B * C", vars);

        result.LeftValue.Should().Be(14);
    }

    [Fact]
    public void Evaluate_AbsFunction()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = -50 };
        var result = _parser.Evaluate("ABS(A)", vars);

        result.LeftValue.Should().Be(50);
    }

    [Fact]
    public void Evaluate_ComplexExpression()
    {
        // total_assets = cash + investments + loans - provisions
        var vars = new Dictionary<string, decimal>
        {
            ["total_assets"] = 500,
            ["cash"] = 100,
            ["investments"] = 200,
            ["loans"] = 300,
            ["provisions"] = 100
        };
        var result = _parser.Evaluate("total_assets = cash + investments + loans - provisions", vars);

        result.Passes.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_UnknownVariable_DefaultsToZero()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 100 };
        var result = _parser.Evaluate("A + unknown_var", vars);

        result.LeftValue.Should().Be(100);
    }

    [Fact]
    public void Evaluate_NoComparison_ShouldJustEvaluateExpression()
    {
        var vars = new Dictionary<string, decimal> { ["A"] = 10, ["B"] = 20 };
        var result = _parser.Evaluate("A + B", vars);

        result.Passes.Should().BeTrue();
        result.LeftValue.Should().Be(30);
        result.RightValue.Should().BeNull();
    }
}
