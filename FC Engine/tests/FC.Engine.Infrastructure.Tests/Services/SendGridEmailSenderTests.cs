using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Notifications;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SendGridEmailSenderTests
{
    [Fact]
    public void Email_Template_Variables_Replaced()
    {
        var template = "Hello {{UserName}}, your return {{ReturnCode}} is ready.";
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = "Ada",
            ["returncode"] = "MFCR 100"
        };

        var output = SendGridEmailSender.ReplaceVariables(template, variables);

        output.Should().Be("Hello Ada, your return MFCR 100 is ready.");
    }

    [Fact]
    public void Email_Sends_With_Tenant_Branding()
    {
        var branding = new BrandingConfig
        {
            CompanyName = "Acme Finance",
            PrimaryColor = "#123456",
            LogoUrl = "https://cdn.acme.test/logo.png",
            SupportEmail = "support@acme.test",
            CopyrightText = "(c) Acme"
        };

        var html = SendGridEmailSender.WrapWithBranding("<p>Body</p>", branding);

        html.Should().Contain("Acme Finance");
        html.Should().Contain("#123456");
        html.Should().Contain("https://cdn.acme.test/logo.png");
        html.Should().Contain("support@acme.test");
        html.Should().Contain("<p>Body</p>");
    }

    [Fact]
    public void Email_Fallback_To_Default_When_No_Branding()
    {
        var html = SendGridEmailSender.WrapWithBranding("<p>Body</p>", new BrandingConfig());

        html.Should().Contain("RegOS");
        html.Should().Contain("#0f766e");
    }
}
