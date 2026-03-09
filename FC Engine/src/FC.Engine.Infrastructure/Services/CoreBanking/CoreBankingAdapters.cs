using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Infrastructure.Services.CoreBanking;

// ============================================================
// Finacle (Infosys) Core Banking Adapter
// Protocol: Finacle EAI REST API with Bearer token auth
// Query types: GL_BALANCE:{glCode} | REPORT:{reportCode}:{column}
// ============================================================
public sealed class FinacleCoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.Finacle;

    private readonly IHttpClientFactory _httpFactory;

    public FinacleCoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)
            ?? throw new InvalidOperationException("Invalid Finacle field mapping JSON.");

        var (year, month) = ParsePeriodCode(periodCode);

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", config.Credential);

        var extractedFields = new Dictionary<string, object?>();
        var unmapped        = new List<string>();

        foreach (var (regoFieldCode, finacleQuery) in mapping)
        {
            try
            {
                var value = await FetchFinacleValueAsync(http, finacleQuery, year, month, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode} (error: {ex.Message})");
            }
        }

        return new CoreBankingExtractionResult(
            Success:         true,
            ModuleCode:      moduleCode,
            PeriodCode:      periodCode,
            ExtractedFields: extractedFields,
            UnmappedFields:  unmapped,
            ErrorMessage:    null,
            ExtractedAt:     DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", config.Credential);

            var response = await http.GetAsync("/eai/api/v1/health", ct);
            sw.Stop();

            return response.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "Connected successfully.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false,
                    $"Health check returned HTTP {(int)response.StatusCode}.",
                    sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchFinacleValueAsync(
        HttpClient http, string query, int year, int month, CancellationToken ct)
    {
        var parts = query.Split(':');

        if (parts[0] == "GL_BALANCE")
        {
            var glCode = parts[1];
            var resp = await http.GetAsync(
                $"/eai/api/v1/gl/balance?glCode={Uri.EscapeDataString(glCode)}" +
                $"&year={year}&month={month:D2}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("closingBalance").GetDecimal();
        }

        if (parts[0] == "REPORT")
        {
            var reportCode = parts[1];
            var column     = parts[2];
            var resp = await http.GetAsync(
                $"/eai/api/v1/reports/{Uri.EscapeDataString(reportCode)}/data" +
                $"?year={year}&month={month:D2}&column={Uri.EscapeDataString(column)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("value").GetDecimal();
        }

        throw new InvalidOperationException($"Unknown Finacle query type: {parts[0]}");
    }

    private static (int Year, int Month) ParsePeriodCode(string periodCode)
    {
        if (periodCode.Contains("-Q"))
        {
            var year    = int.Parse(periodCode[..4]);
            var quarter = int.Parse(periodCode[6..]);
            return (year, (quarter - 1) * 3 + 1);
        }
        var parts = periodCode.Split('-');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}

// ============================================================
// T24 / Transact (Temenos) Core Banking Adapter
// Protocol: T24 Transact REST API with Basic auth (user:pass in Credential)
// Query types: ENQUIRY:{enquiryName}:{fieldName}
// ============================================================
public sealed class T24CoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.T24;

    private readonly IHttpClientFactory _httpFactory;

    public T24CoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)!;

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Add("Authorization",
            $"Basic {Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(config.Credential))}");

        var extractedFields = new Dictionary<string, object?>();
        var unmapped        = new List<string>();

        foreach (var (regoFieldCode, t24Query) in mapping)
        {
            try
            {
                var value = await FetchT24ValueAsync(http, t24Query, periodCode, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode}: {ex.Message}");
            }
        }

        return new CoreBankingExtractionResult(
            true, moduleCode, periodCode, extractedFields,
            unmapped, null, DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Add("Authorization",
                $"Basic {Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(config.Credential))}");

            var resp = await http.GetAsync("/T24/api/v1.0.0/meta/ping", ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "T24 Transact API reachable.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchT24ValueAsync(
        HttpClient http, string query, string periodCode, CancellationToken ct)
    {
        var parts = query.Split(':');
        if (parts[0] == "ENQUIRY")
        {
            var enquiryName = parts[1];
            var fieldName   = parts[2];
            var resp = await http.GetAsync(
                $"/T24/api/v1.0.0/enquiry/{Uri.EscapeDataString(enquiryName)}" +
                $"?period={Uri.EscapeDataString(periodCode)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("body")[0]
                .GetProperty(fieldName)
                .GetDecimal();
        }
        throw new InvalidOperationException($"Unknown T24 query type: {parts[0]}");
    }
}

// ============================================================
// BankOne Core Banking Adapter
// Protocol: BankOne REST API with Ocp-Apim-Subscription-Key header
// Query types: GL:{accountCode}
// ============================================================
public sealed class BankOneCoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.BankOne;

    private readonly IHttpClientFactory _httpFactory;

    public BankOneCoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)!;

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", config.Credential);

        var extractedFields = new Dictionary<string, object?>();
        var unmapped        = new List<string>();

        foreach (var (regoFieldCode, bankOneEndpoint) in mapping)
        {
            try
            {
                var value = await FetchBankOneValueAsync(http, bankOneEndpoint, periodCode, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode}: {ex.Message}");
            }
        }

        return new CoreBankingExtractionResult(
            true, moduleCode, periodCode, extractedFields,
            unmapped, null, DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", config.Credential);

            var resp = await http.GetAsync("/BankOneWebAPI/api/v3/AccountEnquiry/Ping", ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "BankOne API reachable.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchBankOneValueAsync(
        HttpClient http, string query, string periodCode, CancellationToken ct)
    {
        var parts = query.Split(':');
        if (parts[0] == "GL")
        {
            var accountCode = parts[1];
            var resp = await http.GetAsync(
                $"/BankOneWebAPI/api/v3/Reports/GLBalance" +
                $"?accountCode={Uri.EscapeDataString(accountCode)}" +
                $"&period={Uri.EscapeDataString(periodCode)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("data").GetProperty("balance").GetDecimal();
        }
        throw new InvalidOperationException($"Unknown BankOne query type: {parts[0]}");
    }
}

// ============================================================
// Flexcube (Oracle) Core Banking Adapter
// Protocol: Oracle FCUBS REST APIs with Bearer token + appId header
// Query types: GL_INQUIRY:{glCode}
// ============================================================
public sealed class FlexcubeCoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.Flexcube;

    private readonly IHttpClientFactory _httpFactory;

    public FlexcubeCoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)!;

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Add("appId", "FCUBS");
        http.DefaultRequestHeaders.Add("branchCode", "001");
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", config.Credential);

        var extractedFields = new Dictionary<string, object?>();
        var unmapped        = new List<string>();

        foreach (var (regoFieldCode, fcQuery) in mapping)
        {
            try
            {
                var value = await FetchFlexcubeValueAsync(http, fcQuery, periodCode, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode}: {ex.Message}");
            }
        }

        return new CoreBankingExtractionResult(
            true, moduleCode, periodCode, extractedFields,
            unmapped, null, DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Add("appId", "FCUBS");
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", config.Credential);

            var resp = await http.GetAsync("/service/FCUBS/STTM/BANK/SUMMARY", ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "Flexcube API reachable.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchFlexcubeValueAsync(
        HttpClient http, string query, string periodCode, CancellationToken ct)
    {
        var parts = query.Split(':');
        if (parts[0] == "GL_INQUIRY")
        {
            var glCode = parts[1];
            var resp = await http.GetAsync(
                $"/service/FCUBS/GLTM/GLAES/GLMIS_QUERY" +
                $"?gl_code={Uri.EscapeDataString(glCode)}&period={Uri.EscapeDataString(periodCode)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("fcubs-body")
                .GetProperty("Glaes-Details")[0]
                .GetProperty("ACY_AVG_BALANCE")
                .GetDecimal();
        }
        throw new InvalidOperationException($"Unknown Flexcube query type: {parts[0]}");
    }
}

// ============================================================
// Factory — resolves adapter by CoreBankingSystem enum
// ============================================================
public sealed class CoreBankingAdapterFactory : ICoreBankingAdapterFactory
{
    private readonly IReadOnlyDictionary<CoreBankingSystem, ICoreBankingAdapter> _adapters;

    public CoreBankingAdapterFactory(IEnumerable<ICoreBankingAdapter> adapters)
        => _adapters = adapters.ToDictionary(a => a.SystemType);

    public ICoreBankingAdapter GetAdapter(CoreBankingSystem system)
        => _adapters.TryGetValue(system, out var adapter)
            ? adapter
            : throw new InvalidOperationException(
                $"No core banking adapter registered for system '{system}'.");
}
