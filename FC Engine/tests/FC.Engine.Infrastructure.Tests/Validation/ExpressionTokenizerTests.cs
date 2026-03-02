using FC.Engine.Infrastructure.Validation;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Validation;

public class ExpressionTokenizerTests
{
    private readonly ExpressionTokenizer _tokenizer = new();

    [Fact]
    public void Tokenize_SimpleAddition_ShouldReturnThreeTokens()
    {
        var tokens = _tokenizer.Tokenize("A + B");

        tokens.Should().HaveCount(3);
        tokens[0].Should().Be(new Token(TokenType.Variable, "A"));
        tokens[1].Should().Be(new Token(TokenType.Operator, "+"));
        tokens[2].Should().Be(new Token(TokenType.Variable, "B"));
    }

    [Fact]
    public void Tokenize_NumberLiteral_ShouldParseCorrectly()
    {
        var tokens = _tokenizer.Tokenize("100.5");

        tokens.Should().HaveCount(1);
        tokens[0].Type.Should().Be(TokenType.Number);
        tokens[0].Value.Should().Be("100.5");
    }

    [Fact]
    public void Tokenize_ComparisonOperators_ShouldParseTwoCharOps()
    {
        var tokens = _tokenizer.Tokenize("A >= B");

        tokens.Should().HaveCount(3);
        tokens[1].Type.Should().Be(TokenType.Comparison);
        tokens[1].Value.Should().Be(">=");
    }

    [Theory]
    [InlineData(">=")]
    [InlineData("<=")]
    [InlineData("!=")]
    [InlineData("=")]
    [InlineData(">")]
    [InlineData("<")]
    public void Tokenize_AllComparisonTypes_ShouldParse(string op)
    {
        var tokens = _tokenizer.Tokenize($"A {op} B");

        tokens.Should().HaveCount(3);
        tokens[1].Type.Should().Be(TokenType.Comparison);
        tokens[1].Value.Should().Be(op);
    }

    [Fact]
    public void Tokenize_Parentheses_ShouldParse()
    {
        var tokens = _tokenizer.Tokenize("(A + B) * C");

        tokens.Should().HaveCount(7);
        tokens[0].Type.Should().Be(TokenType.LeftParen);
        tokens[4].Type.Should().Be(TokenType.RightParen);
    }

    [Fact]
    public void Tokenize_FunctionCall_ShouldParseAsFunction()
    {
        var tokens = _tokenizer.Tokenize("ABS(A - B)");

        tokens.Should().HaveCount(6);
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
        var tokens = _tokenizer.Tokenize($"{func}(x)");

        tokens[0].Type.Should().Be(TokenType.Function);
        tokens[0].Value.Should().Be(func);
    }

    [Fact]
    public void Tokenize_FieldNameWithUnderscore_ShouldParseAsVariable()
    {
        var tokens = _tokenizer.Tokenize("total_assets = cash_notes + investments");

        tokens.Should().HaveCount(5);
        tokens[0].Should().Be(new Token(TokenType.Variable, "total_assets"));
        tokens[2].Should().Be(new Token(TokenType.Variable, "cash_notes"));
        tokens[4].Should().Be(new Token(TokenType.Variable, "investments"));
    }

    [Fact]
    public void Tokenize_ComplexExpression_ShouldParseAll()
    {
        var tokens = _tokenizer.Tokenize("A + B - C * 0.125 >= D");

        tokens.Should().HaveCount(9);
        tokens[5].Type.Should().Be(TokenType.Number);
        tokens[5].Value.Should().Be("0.125");
    }

    [Fact]
    public void Tokenize_InvalidCharacter_ShouldThrow()
    {
        var act = () => _tokenizer.Tokenize("A @ B");
        act.Should().Throw<ArgumentException>().WithMessage("*Unexpected character*");
    }
}
