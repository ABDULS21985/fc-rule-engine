using FC.Engine.Domain.ValueObjects;
using FluentAssertions;

namespace FC.Engine.Domain.Tests.ValueObjects;

public class BrandingConfigTests
{
    [Fact]
    public void WithDefaults_WhenCustomNull_ReturnsExpectedDefaults()
    {
        var config = BrandingConfig.WithDefaults();

        config.PrimaryColor.Should().Be("#006B3F");
        config.SecondaryColor.Should().Be("#C8A415");
        config.AccentColor.Should().Be("#1A73E8");
        config.FontBody.Should().Be("Inter");
        config.FontHeading.Should().Be("Plus Jakarta Sans");
        config.LogoUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WithDefaults_WhenCustomProvided_MergesProperties()
    {
        var custom = new BrandingConfig
        {
            PrimaryColor = "#0057B8",
            CompanyName = "Zenith Bank",
            SupportEmail = "support@zenith.example"
        };

        var merged = BrandingConfig.WithDefaults(custom);

        merged.PrimaryColor.Should().Be("#0057B8");
        merged.CompanyName.Should().Be("Zenith Bank");
        merged.SupportEmail.Should().Be("support@zenith.example");
        merged.SecondaryColor.Should().Be("#C8A415");
    }
}
