using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Services.DataProtection;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ComplianceTaggerTests
{
    private readonly ComplianceTagger _sut = new();

    [Fact]
    public void GDPR_Email_Includes_Article_4_1_And_Minimization()
    {
        var tags = _sut.Tag(PiiCatalog.Email);

        tags.Should().Contain(x => x.Framework == "gdpr" && x.Article == "Art. 4(1)");
        tags.Should().Contain(x => x.Framework == "gdpr" && x.Article == "Art. 5(1)(c)");
    }

    [Fact]
    public void GDPR_Health_Maps_To_Article_9()
    {
        var tags = _sut.Tag(PiiCatalog.Health);

        tags.Should().Contain(x => x.Framework == "gdpr" && x.Article == "Art. 9");
    }

    [Fact]
    public void HIPAA_PHI_Maps_Name_And_Health_Correctly()
    {
        var tags = _sut.Tag(PiiCatalog.Name).Concat(_sut.Tag(PiiCatalog.Health)).ToList();

        tags.Should().Contain(x => x.Framework == "hipaa" && x.Article == "§164.514(b)");
        tags.Should().Contain(x => x.Framework == "hipaa" && x.Article == "§164.530(c)");
    }

    [Fact]
    public void HIPAA_NonPhi_CreditCard_Has_No_Hipaa_Tag()
    {
        var tags = _sut.Tag(PiiCatalog.CreditCard);

        tags.Should().NotContain(x => x.Framework == "hipaa");
    }

    [Fact]
    public void SOC2_All_Pii_Types_Have_At_Least_One_Tag()
    {
        foreach (var piiType in PiiCatalog.AllTypes)
        {
            var tags = _sut.Tag(piiType);
            tags.Should().Contain(x => x.Framework == "soc2", because: piiType);
        }
    }

    [Fact]
    public void PCI_CreditCard_Maps_To_Req_3_4()
    {
        var tags = _sut.Tag(PiiCatalog.CreditCard);

        tags.Should().Contain(x => x.Framework == "pci_dss" && x.Article == "Req 3.4");
    }

    [Fact]
    public void SaudiSensitiveFields_Map_To_Article_11()
    {
        foreach (var piiType in new[] { PiiCatalog.DateOfBirth, PiiCatalog.Health, PiiCatalog.Religion })
        {
            var tags = _sut.Tag(piiType);
            tags.Should().Contain(x => x.Framework == "saudi_pdpl" && x.Article == "Art. 11", because: piiType);
        }
    }

    [Fact]
    public void All_Pii_Types_Have_Gdpr_And_Soc2_Coverage()
    {
        foreach (var piiType in PiiCatalog.AllTypes)
        {
            var tags = _sut.Tag(piiType);
            tags.Should().Contain(x => x.Framework == "gdpr", because: piiType);
            tags.Should().Contain(x => x.Framework == "soc2", because: piiType);
        }
    }
}
