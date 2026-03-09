using System.Text.RegularExpressions;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class PiiClassifier
{
    private static readonly Regex EmailSamplePattern = Build(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    private static readonly Regex PhoneSamplePattern = Build(@"^\+?[0-9][0-9\-\s]{7,}$");
    private static readonly Regex SsnSamplePattern = Build(@"^\d{3}-\d{2}-\d{4}$");
    private static readonly Regex CreditCardSamplePattern = Build(@"^\d{13,19}$");
    private static readonly Regex IpAddressSamplePattern = Build(@"^(\d{1,3}\.){3}\d{1,3}$");
    private static readonly Regex BvnSamplePattern = Build(@"^\d{11}$");

    private static readonly IReadOnlyList<(Regex Pattern, string PiiType)> Rules =
    [
        (Build("email|e_mail"), PiiCatalog.Email),
        (Build("phone|mobile|msisdn|telephone"), PiiCatalog.Phone),
        (Build("first.?name|last.?name|full.?name|surname|customer.?name|beneficiary.?name|name"), PiiCatalog.Name),
        (Build("address|street|city|state|zip|postal"), PiiCatalog.Address),
        (Build("date.?of.?birth|birth.?date|\\bdob\\b"), PiiCatalog.DateOfBirth),
        (Build("social.?security|\\bssn\\b"), PiiCatalog.Ssn),
        (Build("national.?id|passport.?number|tax.?id|\\bnin\\b"), PiiCatalog.NationalId),
        (Build("health|medical|diagnosis|condition|treatment|patient"), PiiCatalog.Health),
        (Build("medical"), PiiCatalog.Medical),
        (Build("credit.?card|card.?number|\\bpan\\b"), PiiCatalog.CreditCard),
        (Build("bank.?account|account.?number|iban|bban"), PiiCatalog.BankAccount),
        (Build("salary|payroll|compensation|wage"), PiiCatalog.Salary),
        (Build("password|secret|credential|api.?key|token"), PiiCatalog.Credential),
        (Build("gender|\\bsex\\b"), PiiCatalog.Gender),
        (Build("ethnicity|race"), PiiCatalog.Ethnicity),
        (Build("religion|faith"), PiiCatalog.Religion),
        (Build("biometric|fingerprint|faceprint|iris"), PiiCatalog.Biometric),
        (Build("ip.?address|client.?ip|remote.?ip|\\bip\\b"), PiiCatalog.IpAddress),
        (Build("\\bbvn\\b"), PiiCatalog.Bvn)
    ];

    public IReadOnlyList<string> Classify(string tableName, DataColumnSchema column)
    {
        var subject = $"{tableName} {column.ColumnName}".Trim();
        var matches = Rules
            .Where(rule => rule.Pattern.IsMatch(subject))
            .Select(rule => rule.PiiType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Contains(PiiCatalog.Health, StringComparer.OrdinalIgnoreCase)
            && !matches.Contains(PiiCatalog.Medical, StringComparer.OrdinalIgnoreCase)
            && column.ColumnName.Contains("medical", StringComparison.OrdinalIgnoreCase))
        {
            matches.Add(PiiCatalog.Medical);
        }

        foreach (var sample in column.SampleValues.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var normalized = sample.Trim();
            if (EmailSamplePattern.IsMatch(normalized))
            {
                matches.Add(PiiCatalog.Email);
            }
            if (PhoneSamplePattern.IsMatch(normalized))
            {
                matches.Add(PiiCatalog.Phone);
            }
            if (SsnSamplePattern.IsMatch(normalized))
            {
                matches.Add(PiiCatalog.Ssn);
            }
            if (CreditCardSamplePattern.IsMatch(normalized) && PassesLuhn(normalized))
            {
                matches.Add(PiiCatalog.CreditCard);
            }
            if (IpAddressSamplePattern.IsMatch(normalized))
            {
                matches.Add(PiiCatalog.IpAddress);
            }
            if (BvnSamplePattern.IsMatch(normalized))
            {
                matches.Add(PiiCatalog.Bvn);
            }
        }

        return matches;
    }

    public DataSensitivityLevel ClassifySensitivity(IReadOnlyCollection<string> piiTypes)
    {
        if (piiTypes.Count == 0)
        {
            return DataSensitivityLevel.Internal;
        }

        if (piiTypes.Any(t => t is PiiCatalog.DateOfBirth
                              or PiiCatalog.Ssn
                              or PiiCatalog.NationalId
                              or PiiCatalog.Health
                              or PiiCatalog.Medical
                              or PiiCatalog.Credential
                              or PiiCatalog.Religion
                              or PiiCatalog.Biometric
                              or PiiCatalog.Ethnicity
                              or PiiCatalog.Gender))
        {
            return DataSensitivityLevel.Restricted;
        }

        if (piiTypes.Any(t => t is PiiCatalog.CreditCard
                              or PiiCatalog.BankAccount
                              or PiiCatalog.Salary
                              or PiiCatalog.Bvn))
        {
            return DataSensitivityLevel.Confidential;
        }

        return DataSensitivityLevel.Internal;
    }

    private static Regex Build(string pattern)
        => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool PassesLuhn(string value)
    {
        var sum = 0;
        var alternate = false;

        for (var i = value.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(value[i]))
            {
                return false;
            }

            var digit = value[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}
