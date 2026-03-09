using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public interface IFrameworkTagger
{
    string Framework();
    IReadOnlyList<ComplianceTag> Tag(string piiType);
}

public sealed class ComplianceTagger
{
    private readonly IReadOnlyList<IFrameworkTagger> _taggers;

    public ComplianceTagger()
        : this(
        [
            new GdprTagger(),
            new HipaaTagger(),
            new Soc2Tagger(),
            new PciDssTagger(),
            new SaudiPdplTagger()
        ])
    {
    }

    public ComplianceTagger(IReadOnlyList<IFrameworkTagger> taggers) => _taggers = taggers;

    public IReadOnlyList<ComplianceTag> Tag(string piiType)
    {
        var normalized = Normalize(piiType);
        return _taggers
            .SelectMany(t => t.Tag(normalized))
            .GroupBy(t => $"{t.Framework}|{t.Article}|{t.Category}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t.Framework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Article, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Normalize(string piiType)
    {
        var normalized = piiType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ssn/national_id" => PiiCatalog.Ssn,
            "health/medical" => PiiCatalog.Health,
            "gender/ethnicity" => PiiCatalog.Gender,
            "bvn (nigeria)" => PiiCatalog.Bvn,
            _ => normalized
        };
    }
}

internal sealed class GdprTagger : IFrameworkTagger
{
    private static readonly Dictionary<string, IReadOnlyList<ComplianceTag>> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        [PiiCatalog.Email] = Personal("Art. 4(1)", "personal_data", "Requires lawful basis for processing", "Subject to access, rectification, and erasure rights.", "medium"),
        [PiiCatalog.Phone] = Personal("Art. 4(1)", "personal_data", "Requires lawful basis for processing", "Direct contact data must be protected and minimized.", "medium"),
        [PiiCatalog.Name] = Personal("Art. 4(1)", "personal_data", "Requires lawful basis for processing", "Identifiable natural person data is regulated.", "medium"),
        [PiiCatalog.Address] = Personal("Art. 4(1)", "personal_data", "Requires lawful basis for processing", "Location and residency data must be protected.", "medium"),
        [PiiCatalog.DateOfBirth] = Special("Art. 9", "special_category", "Requires heightened safeguards and explicit lawful basis", "Sensitive personal attribute handling applies.", "high"),
        [PiiCatalog.Ssn] = [Tag("gdpr", "Art. 87", "national_identifier", "National identification numbers require specific safeguards", "Government identifier handling must be tightly controlled.", "high"), .. CrossCutting()],
        [PiiCatalog.NationalId] = [Tag("gdpr", "Art. 87", "national_identifier", "National identification numbers require specific safeguards", "Government identifier handling must be tightly controlled.", "high"), .. CrossCutting()],
        [PiiCatalog.Health] = Special("Art. 9", "special_category", "Processing health data requires special category safeguards", "Explicit basis and enhanced controls are mandatory.", "high"),
        [PiiCatalog.Medical] = Special("Art. 9", "special_category", "Processing medical data requires special category safeguards", "Explicit basis and enhanced controls are mandatory.", "high"),
        [PiiCatalog.CreditCard] = Personal("Art. 4(1)", "personal_data", "Payment data remains personal data under GDPR", "Masking and security controls must apply.", "high"),
        [PiiCatalog.BankAccount] = Personal("Art. 4(1)", "personal_data", "Financial identifiers require lawful processing basis", "Unauthorized disclosure can cause direct harm.", "high"),
        [PiiCatalog.Salary] = Personal("Art. 4(1)", "personal_data", "Compensation data requires access minimization", "Confidential payroll handling is required.", "high"),
        [PiiCatalog.Credential] = [Tag("gdpr", "Art. 32", "security", "Appropriate technical and organizational security measures are required", "Authentication secrets require strong protection.", "high"), .. CrossCutting()],
        [PiiCatalog.Gender] = Special("Art. 9", "special_category", "Sensitive identity traits require special category safeguards", "Potential discrimination risk raises compliance obligations.", "high"),
        [PiiCatalog.Ethnicity] = Special("Art. 9", "special_category", "Sensitive identity traits require special category safeguards", "Potential discrimination risk raises compliance obligations.", "high"),
        [PiiCatalog.Religion] = Special("Art. 9", "special_category", "Religious belief data requires special category safeguards", "Explicit basis and enhanced controls are mandatory.", "high"),
        [PiiCatalog.Biometric] = Special("Art. 9", "special_category", "Biometric data requires special category safeguards", "Biometric misuse has irreversible impact.", "high"),
        [PiiCatalog.IpAddress] = Personal("Art. 4(1)", "personal_data", "Online identifiers can constitute personal data", "Network identifiers require lawful use and retention limits.", "medium"),
        [PiiCatalog.Bvn] = Personal("Art. 4(1)", "personal_data", "Bank verification identifiers require lawful processing basis", "National banking identifiers require strong protection.", "high")
    };

    public string Framework() => "gdpr";
    public IReadOnlyList<ComplianceTag> Tag(string piiType) => Tags.GetValueOrDefault(piiType, CrossCutting());

    private static IReadOnlyList<ComplianceTag> Personal(string article, string category, string requirement, string impact, string severity)
        => [Tag("gdpr", article, category, requirement, impact, severity), .. CrossCutting()];

    private static IReadOnlyList<ComplianceTag> Special(string article, string category, string requirement, string impact, string severity)
        => [Tag("gdpr", article, category, requirement, impact, severity), .. CrossCutting()];

    private static IReadOnlyList<ComplianceTag> CrossCutting()
        =>
        [
            Tag("gdpr", "Art. 5(1)(c)", "data_minimization", "Personal data collected must be adequate, relevant, and limited", "Collection scope must remain proportionate.", "medium"),
            Tag("gdpr", "Art. 5(1)(e)", "storage_limitation", "Personal data must not be kept longer than necessary", "Retention schedules and deletion controls are required.", "medium")
        ];

    private static ComplianceTag Tag(string framework, string article, string category, string requirement, string impact, string severity)
        => new() { Framework = framework, Article = article, Category = category, Requirement = requirement, Impact = impact, Severity = severity };
}

internal sealed class HipaaTagger : IFrameworkTagger
{
    private static readonly Dictionary<string, IReadOnlyList<ComplianceTag>> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        [PiiCatalog.Email] = [Tag("§164.514(b)", "phi_identifier", "Identifiers must be removed or protected for de-identification", "Can identify an individual in healthcare context.", "medium")],
        [PiiCatalog.Phone] = [Tag("§164.514(b)", "phi_identifier", "Identifiers must be removed or protected for de-identification", "Telephone identifiers are protected in PHI context.", "medium")],
        [PiiCatalog.Name] = [Tag("§164.514(b)", "phi_identifier", "Identifiers must be removed or protected for de-identification", "Personal names are protected in PHI context.", "medium")],
        [PiiCatalog.Address] = [Tag("§164.514(b)", "phi_identifier", "Identifiers must be removed or protected for de-identification", "Location identifiers are protected in PHI context.", "medium")],
        [PiiCatalog.DateOfBirth] = [Tag("§164.514(b)", "phi_identifier", "Dates tied to individuals require de-identification safeguards", "Birth dates can directly re-identify a patient.", "high")],
        [PiiCatalog.Ssn] = [Tag("§164.514(b)", "phi_identifier", "Government identifiers require de-identification safeguards", "Identity theft and patient re-identification risk is high.", "high")],
        [PiiCatalog.NationalId] = [Tag("§164.514(b)", "phi_identifier", "Government identifiers require de-identification safeguards", "Identity theft and patient re-identification risk is high.", "high")],
        [PiiCatalog.Health] = [Tag("§164.530(c)", "phi", "Covered entities must protect PHI with administrative, physical, and technical safeguards", "Clinical data disclosure is highly regulated.", "high")],
        [PiiCatalog.Medical] = [Tag("§164.530(c)", "phi", "Covered entities must protect PHI with administrative, physical, and technical safeguards", "Clinical data disclosure is highly regulated.", "high")],
        [PiiCatalog.Biometric] = [Tag("§164.514(b)", "phi_identifier", "Biometric identifiers require de-identification safeguards", "Biometrics uniquely identify a patient.", "high")]
    };

    public string Framework() => "hipaa";
    public IReadOnlyList<ComplianceTag> Tag(string piiType) => Tags.GetValueOrDefault(piiType, []);

    private static ComplianceTag Tag(string article, string category, string requirement, string impact, string severity)
        => new() { Framework = "hipaa", Article = article, Category = category, Requirement = requirement, Impact = impact, Severity = severity };
}

internal sealed class Soc2Tagger : IFrameworkTagger
{
    public string Framework() => "soc2";

    public IReadOnlyList<ComplianceTag> Tag(string piiType)
    {
        var tags = new List<ComplianceTag>
        {
            Tag("CC6.1", "access_control", "Logical access to information assets must be restricted", "All personal data requires role-based access control.", "medium"),
            Tag("CC6.7", "encryption", "Sensitive data must be protected during transmission and storage", "Encryption controls must protect data at rest and in transit.", "high")
        };

        if (piiType.Equals(PiiCatalog.Credential, StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(Tag("CC6.2", "authentication", "Authentication mechanisms must verify identities before access", "Credentials require tighter lifecycle and rotation controls.", "high"));
        }

        if (piiType is PiiCatalog.Ssn or PiiCatalog.NationalId or PiiCatalog.Health or PiiCatalog.Medical or PiiCatalog.Biometric)
        {
            tags.Add(Tag("CC6.7", "restricted_processing", "Highly sensitive information requires enhanced protective controls", "Additional monitoring and segregation are expected.", "high"));
        }

        return tags
            .GroupBy(t => $"{t.Article}|{t.Category}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static ComplianceTag Tag(string article, string category, string requirement, string impact, string severity)
        => new() { Framework = "soc2", Article = article, Category = category, Requirement = requirement, Impact = impact, Severity = severity };
}

internal sealed class PciDssTagger : IFrameworkTagger
{
    public string Framework() => "pci_dss";

    public IReadOnlyList<ComplianceTag> Tag(string piiType)
    {
        var normalized = piiType.Trim().ToLowerInvariant();
        if (normalized == PiiCatalog.CreditCard)
        {
            return [Tag("Req 3.4", "cardholder_data", "Primary account number must be rendered unreadable where stored", "Masking or tokenization controls are mandatory.", "high")];
        }

        if (normalized == PiiCatalog.Credential)
        {
            return [Tag("Req 8.2", "authentication", "Authentication factors and credentials must be protected", "Compromise of credentials can expose cardholder systems.", "high")];
        }

        return [];
    }

    private static ComplianceTag Tag(string article, string category, string requirement, string impact, string severity)
        => new() { Framework = "pci_dss", Article = article, Category = category, Requirement = requirement, Impact = impact, Severity = severity };
}

internal sealed class SaudiPdplTagger : IFrameworkTagger
{
    public string Framework() => "saudi_pdpl";

    public IReadOnlyList<ComplianceTag> Tag(string piiType)
    {
        if (piiType is PiiCatalog.DateOfBirth
            or PiiCatalog.Health
            or PiiCatalog.Medical
            or PiiCatalog.Religion
            or PiiCatalog.Biometric
            or PiiCatalog.Ssn
            or PiiCatalog.NationalId
            or PiiCatalog.Gender
            or PiiCatalog.Ethnicity)
        {
            return
            [
                Tag("Art. 11", "sensitive_data", "Sensitive personal data requires stricter safeguards and restrictions", "Additional controls and narrower processing purposes apply.", "high"),
                SecuritySafeguard()
            ];
        }

        if (piiType == PiiCatalog.Credential)
        {
            return [Tag("Art. 18", "security", "Controllers must implement appropriate security safeguards", "Authentication secrets require strong protection.", "high")];
        }

        return
        [
            Tag("Art. 5", "personal_data", "Personal data must be processed fairly and for legitimate purposes", "General personal data obligations apply.", "medium"),
            SecuritySafeguard()
        ];
    }

    private static ComplianceTag SecuritySafeguard()
        => Tag("Art. 18", "security", "Controllers must implement appropriate security safeguards", "Security-by-design controls must protect personal data.", "high");

    private static ComplianceTag Tag(string article, string category, string requirement, string impact, string severity)
        => new() { Framework = "saudi_pdpl", Article = article, Category = category, Requirement = requirement, Impact = impact, Severity = severity };
}
