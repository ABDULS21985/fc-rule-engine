namespace FC.Engine.Infrastructure.Services;

public static class SanctionsWatchlistBaselineCatalog
{
    private static readonly IReadOnlyList<SanctionsCatalogSourceInput> Sources =
    [
        new() { SourceCode = "UN", SourceName = "UN Security Council Consolidated List", RefreshCadence = "Daily", Status = "active" },
        new() { SourceCode = "OFAC", SourceName = "OFAC SDN List", RefreshCadence = "Daily", Status = "active" },
        new() { SourceCode = "EU", SourceName = "EU Consolidated List", RefreshCadence = "Daily", Status = "active" },
        new() { SourceCode = "UK", SourceName = "UK HMT Sanctions List", RefreshCadence = "Daily", Status = "active" },
        new() { SourceCode = "NFIU", SourceName = "Nigeria NFIU / EFCC Domestic List", RefreshCadence = "Daily", Status = "active" },
        new() { SourceCode = "INTERPOL", SourceName = "Interpol Red Notices", RefreshCadence = "Daily", Status = "active" },
        new() { SourceCode = "PEP", SourceName = "Politically Exposed Persons", RefreshCadence = "Daily", Status = "active" }
    ];

    private static readonly IReadOnlyList<SanctionsCatalogEntryInput> Entries =
    [
        new() { SourceCode = "UN", PrimaryName = "AL-QAIDA", Aliases = ["AL QAIDA", "QAEDA", "ALQAIDA"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "UN", PrimaryName = "ISLAMIC STATE", Aliases = ["ISIS", "ISIL", "DAESH"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "OFAC", PrimaryName = "BOKO HARAM", Aliases = ["JAMA'ATU AHLIS SUNNA LIDDA'AWATI WAL-JIHAD"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "OFAC", PrimaryName = "HEZBOLLAH", Aliases = ["HIZBALLAH", "HIZBULLAH"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "EU", PrimaryName = "HAMAS", Aliases = ["HARAKAT AL-MUQAWAMA AL-ISLAMIYYA"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "UK", PrimaryName = "WAGNER GROUP", Aliases = ["PMC WAGNER", "WAGNER"], Category = "entity", RiskLevel = "high" },
        new() { SourceCode = "NFIU", PrimaryName = "TERROR FINANCE WATCH", Aliases = ["TFS HIGH RISK COUNTERPARTY"], Category = "entity", RiskLevel = "high" },
        new() { SourceCode = "INTERPOL", PrimaryName = "JOSE RODRIGUEZ", Aliases = ["JOSE MANUEL RODRIGUEZ"], Category = "person", RiskLevel = "high" },
        new() { SourceCode = "INTERPOL", PrimaryName = "MARIA IVANOVA", Aliases = ["MARIA PETROVNA IVANOVA"], Category = "person", RiskLevel = "high" },
        new() { SourceCode = "PEP", PrimaryName = "MINISTER OF FINANCE", Aliases = ["HONOURABLE MINISTER OF FINANCE"], Category = "person", RiskLevel = "medium" },
        new() { SourceCode = "PEP", PrimaryName = "CENTRAL BANK GOVERNOR", Aliases = ["GOVERNOR OF THE CENTRAL BANK"], Category = "person", RiskLevel = "medium" },
        new() { SourceCode = "UN", PrimaryName = "ANSARU", Aliases = ["JAMA'ATU ANSARUL MUSLIMINA FI BILADIS SUDAN"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "OFAC", PrimaryName = "AL-SHABAAB", Aliases = ["AL SHABAAB", "HARAKAT AL SHABAAB AL MUJAHIDEEN"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "EU", PrimaryName = "LASHKAR-E-TAIBA", Aliases = ["LASHKAR E TAIBA", "LET"], Category = "entity", RiskLevel = "critical" },
        new() { SourceCode = "UK", PrimaryName = "ISLAMIC JIHAD", Aliases = ["PALESTINIAN ISLAMIC JIHAD"], Category = "entity", RiskLevel = "high" },
        new() { SourceCode = "NFIU", PrimaryName = "DOMESTIC AML WATCH SUBJECT", Aliases = ["NIGERIA AML WATCH SUBJECT"], Category = "person", RiskLevel = "high" }
    ];

    public static SanctionsCatalogMaterializationRequest CreateRequest()
    {
        return new SanctionsCatalogMaterializationRequest
        {
            Sources = Sources
                .Select(x => new SanctionsCatalogSourceInput
                {
                    SourceCode = x.SourceCode,
                    SourceName = x.SourceName,
                    RefreshCadence = x.RefreshCadence,
                    Status = x.Status
                })
                .ToList(),
            Entries = Entries
                .Select(x => new SanctionsCatalogEntryInput
                {
                    SourceCode = x.SourceCode,
                    PrimaryName = x.PrimaryName,
                    Aliases = x.Aliases.ToList(),
                    Category = x.Category,
                    RiskLevel = x.RiskLevel
                })
                .ToList()
        };
    }
}
