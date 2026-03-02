using FC.Engine.Domain.ValueObjects;
using FluentAssertions;

namespace FC.Engine.Domain.Tests.ValueObjects;

public class ReturnCodeTests
{
    [Theory]
    [InlineData("MFCR 300", "MFCR", "300")]
    [InlineData("QFCR 364", "QFCR", "364")]
    [InlineData("SFCR 400", "SFCR", "400")]
    [InlineData("FC 100", "FC", "100")]
    [InlineData("mfcr 300", "MFCR", "300")]
    [InlineData("  MFCR  300  ", "MFCR", "300")]
    public void Parse_ShouldExtractPrefixAndNumber(string input, string expectedPrefix, string expectedNumber)
    {
        var rc = ReturnCode.Parse(input);

        rc.Prefix.Should().Be(expectedPrefix);
        rc.Number.Should().Be(expectedNumber);
    }

    [Theory]
    [InlineData("MFCR 300", "MFCR 300")]
    [InlineData("mfcr300", "MFCR 300")]
    [InlineData("FC 100", "FC 100")]
    public void Parse_ShouldNormalizeValue(string input, string expected)
    {
        var rc = ReturnCode.Parse(input);
        rc.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("MFCR 300", "mfcr_300")]
    [InlineData("QFCR 364", "qfcr_364")]
    [InlineData("FC 100", "fc_100")]
    public void ToTableName_ShouldProduceLowercaseUnderscored(string input, string expected)
    {
        var rc = ReturnCode.Parse(input);
        rc.ToTableName().Should().Be(expected);
    }

    [Theory]
    [InlineData("MFCR 300", "MFCR300")]
    [InlineData("QFCR 364", "QFCR364")]
    public void ToXmlRootElement_ShouldRemoveSpaces(string input, string expected)
    {
        var rc = ReturnCode.Parse(input);
        rc.ToXmlRootElement().Should().Be(expected);
    }

    [Theory]
    [InlineData("MFCR 300", "urn:cbn:dfis:fc:mfcr300")]
    [InlineData("FC 100", "urn:cbn:dfis:fc:fc100")]
    public void ToXmlNamespace_ShouldGenerateUrn(string input, string expected)
    {
        var rc = ReturnCode.Parse(input);
        rc.ToXmlNamespace().Should().Be(expected);
    }

    [Fact]
    public void Equals_ShouldReturnTrueForSameCode()
    {
        var a = ReturnCode.Parse("MFCR 300");
        var b = ReturnCode.Parse("mfcr300");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalseForDifferentCodes()
    {
        var a = ReturnCode.Parse("MFCR 300");
        var b = ReturnCode.Parse("MFCR 301");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_ShouldReturnValue()
    {
        var rc = ReturnCode.Parse("MFCR 300");
        rc.ToString().Should().Be("MFCR 300");
    }
}
