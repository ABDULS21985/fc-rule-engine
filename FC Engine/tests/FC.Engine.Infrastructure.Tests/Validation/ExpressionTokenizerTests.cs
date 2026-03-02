using FC.Engine.Infrastructure.Validation;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Validation;

public class ExpressionTokenizerTests
{
    private readonly ExpressionTokenizer _tokenizer = new();

    [Fact]
    public void Tokenize_SimpleAddition_ShouldProduceThreeTokens()
    {
        var tokens = _tokenizer.Tokenize("A + B");

        tokens.Should().HaveCount(3);
        tokens[0].Should().Be(new Token(TokenType.Variable, "A"));
        tokens[1].Should().Be(new Token(TokenType.Operator, "+"));
        tokens[2].Should().Be(new Token(TokenType.Variable, "B"));
    }

    [Fact]
    public void Tokenize_NumbersAndVariables_ShouldDistinguish()
    {
        var tokens = _tokenizer.Tokenize("total_assets = 100.50");

        tokens.Should().HaveCount(3);
        tokens[0].Type.Should().Be(TokenType.Variable);
        tokens[0].Value.Should().Be("total_assets");
        tokens[1].Type.Should().Be(TokenType.Comparison);
        tokens[1].Value.Should().Be("=");
        tokens[2].Type.Should().Be(TokenType.Number);
        tokens[2].Value.Should().Be("100.50");
    }

    [Fact]
    public void Tokenize_TwoCharOperators_ShouldRecognize()
    {
        var tokens = _tokenizer.Tokenize("A >= B");

        tokens[1].Type.Should().Be(TokenType.Comparison);
        tokens[1].Value.Should().Be(">=");
    }

    [Theory]
    [InlineData(">=")]
    [InlineData("<=")]
    [InlineData("!=")]
    public void Tokenize_AllTwoCharComparisons(string op)
    {
        var tokens = _tokenizer.Tokenize($"A {op} B");
        tokens[1].Value.Should().Be(op);
    }

    [Theory]
    [InlineData(">")]
    [InlineData("<")]
    [InlineData("=")]
    public void Tokenize_SingleCharComparisons(string op)
    {
        var tokens = _tokenizer.Tokenize($"X {op} Y");
        tokens[1].Type.Should().Be(TokenType.Comparison);
        tokens[1].Value.Should().Be(op);
    }

    [Fact]
    public void Tokenize_Parentheses_ShouldRecognize()
    {
        var tokens = _tokenizer.Tokenize("(A + B) * C");

        tokens.Should().HaveCount(7);
        tokens[0].Type.Should().Be(TokenType.LeftParen);
        tokens[4].Type.Should().Be(TokenType.RightParen);
    }

    [Fact]
    public void Tokenize_Functions_ShouldRecognize()
    {
        var tokens = _tokenizer.Tokenize("ABS(A - B)");

        tokens[0].Type.Should().Be(TokenType.Function);
        tokens[0].Value.Should().Be("ABS");
    }

    [Theory]
    [InlineData("SUM")]
    [InlineData("COUNT")]
    [InlineData("MAX")]
    [InlineData("MIN")]
    [InlineData("AVG")]
    [InlineData("ABS")]
    public void Tokenize_AllFunctions_ShouldRecognize(string func)
    {
        var tokens = _tokenizer.Tokenize($"{func}(X)");
        tokens[0].Type.Should().Be(TokenType.Function);
        tokens[0].Value.Should().Be(func);
    }

    [Fact]
    public void Tokenize_ComplexExpression()
    {
        var tokens = _tokenizer.Tokenize("A + B - C >= D * 0.125");

        tokens.Should().HaveCount(9);
        tokens.Select(t => t.Type).Should().ContainInOrder(
            TokenType.Variable, TokenType.Operator, TokenType.Variable,
            TokenType.Operator, TokenType.Variable, TokenType.Comparison,
            TokenType.Variable, TokenType.Operator, TokenType.Number);
    }

    [Fact]
    public void Tokenize_UnexpectedCharacter_ShouldThrow()
    {
        var act = () => _tokenizer.Tokenize("A @ B");
        act.Should().Throw<ArgumentException>().WithMessage("*@*");
    }

    [Fact]
    public void Tokenize_EmptyExpression_ShouldReturnEmpty()
    {
        var tokens = _tokenizer.Tokenize("   ");
        tokens.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_FieldNamesWithUnderscores()
    {
        var tokens = _tokenizer.Tokenize("cash_and_balances_with_cbn + due_from_banks");
        tokens.Should().HaveCount(3);
        tokens[0].Value.Should().Be("cash_and_balances_with_cbn");
        tokens[2].Value.Should().Be("due_from_banks");
    }
}
