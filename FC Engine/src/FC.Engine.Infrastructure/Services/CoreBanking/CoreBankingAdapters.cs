using System.Net.Http.Json;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services.CoreBanking;

/// <summary>
/// Adapter for Infosys Finacle core banking system.
/// Extracts GL balances and maps them to return template fields.
/// </summary>
public class FinacleAdapter : ICoreBankingAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FinacleAdapter> _logger;

    public string AdapterName => "Finacle";

    public FinacleAdapter(IHttpClientFactory httpClientFactory, ILogger<FinacleAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CoreBankingExtractResult> ExtractReturnData(
        string moduleCode, string periodCode, CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        _logger.LogInformation("Finacle: Extracting data for module {Module}, period {Period}", moduleCode, periodCode);

        var result = new CoreBankingExtractResult { Success = true };

        try
        {
            var client = _httpClientFactory.CreateClient("FinacleClient");
            client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));

            if (!string.IsNullOrWhiteSpace(config.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);

            // Finacle REST API: query GL balances for the reporting period
            // GET /api/gl/balances?branch=ALL&period={periodCode}&module={moduleCode}
            var endpoint = $"/api/gl/balances?branch=ALL&period={Uri.EscapeDataString(periodCode)}&module={Uri.EscapeDataString(moduleCode)}";
            var response = await client.GetAsync(endpoint, ct);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Finacle API returned {response.StatusCode}";
                return result;
            }

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);
            if (data != null)
                result.FieldValues = data;

            result.Warnings.Add("Finacle adapter: verify GL account mapping matches template fields");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finacle extraction failed for {Module}/{Period}", moduleCode, periodCode);
            result.Success = false;
            result.ErrorMessage = $"Finacle connection error: {ex.Message}";
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("FinacleClient");
            client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));

            if (!string.IsNullOrWhiteSpace(config.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);

            var response = await client.GetAsync("/api/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Adapter for Temenos T24/Transact core banking system.
/// </summary>
public class T24Adapter : ICoreBankingAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<T24Adapter> _logger;

    public string AdapterName => "T24";

    public T24Adapter(IHttpClientFactory httpClientFactory, ILogger<T24Adapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CoreBankingExtractResult> ExtractReturnData(
        string moduleCode, string periodCode, CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        _logger.LogInformation("T24: Extracting data for module {Module}, period {Period}", moduleCode, periodCode);

        var result = new CoreBankingExtractResult { Success = true };

        try
        {
            var client = _httpClientFactory.CreateClient("T24Client");
            client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));

            if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            // T24 IRIS API: query account balances
            var endpoint = $"/api/v1.0.0/party/customers/balances?period={Uri.EscapeDataString(periodCode)}";
            var response = await client.GetAsync(endpoint, ct);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"T24 API returned {response.StatusCode}";
                return result;
            }

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);
            if (data != null)
                result.FieldValues = data;

            result.Warnings.Add("T24 adapter: verify IRIS field mapping matches template fields");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "T24 extraction failed for {Module}/{Period}", moduleCode, periodCode);
            result.Success = false;
            result.ErrorMessage = $"T24 connection error: {ex.Message}";
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("T24Client");
            client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));
            var response = await client.GetAsync("/api/v1.0.0/system/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Adapter for BankOne core banking system — direct database connector.
/// </summary>
public class BankOneAdapter : ICoreBankingAdapter
{
    private readonly ILogger<BankOneAdapter> _logger;

    public string AdapterName => "BankOne";

    public BankOneAdapter(ILogger<BankOneAdapter> logger)
    {
        _logger = logger;
    }

    public async Task<CoreBankingExtractResult> ExtractReturnData(
        string moduleCode, string periodCode, CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        _logger.LogInformation("BankOne: Extracting data for module {Module}, period {Period}", moduleCode, periodCode);

        var result = new CoreBankingExtractResult { Success = true };

        try
        {
            if (string.IsNullOrWhiteSpace(config.DatabaseConnectionString))
            {
                result.Success = false;
                result.ErrorMessage = "BankOne adapter requires a database connection string";
                return result;
            }

            // BankOne uses direct SQL queries to extract GL data
            // Query pattern: SELECT GLCode, Balance, Currency FROM GL_Balances
            //                WHERE ReportingPeriod = @periodCode
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(config.DatabaseConnectionString);
            await connection.OpenAsync(ct);

            var sql = @"
                SELECT gl.GLCode, gl.Balance, gl.Currency, gl.BranchCode
                FROM GL_Balances gl
                WHERE gl.ReportingPeriod = @PeriodCode
                ORDER BY gl.GLCode";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@PeriodCode", periodCode);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var glCode = reader.GetString(0);
                var balance = reader.GetDecimal(1);
                result.FieldValues[glCode] = balance;
            }

            result.Warnings.Add("BankOne adapter: verify GL code mapping matches template field names");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BankOne extraction failed for {Module}/{Period}", moduleCode, periodCode);
            result.Success = false;
            result.ErrorMessage = $"BankOne database error: {ex.Message}";
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.DatabaseConnectionString))
                return false;

            using var connection = new Microsoft.Data.SqlClient.SqlConnection(config.DatabaseConnectionString);
            await connection.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Adapter for Oracle Flexcube core banking system — API integration.
/// </summary>
public class FlexcubeAdapter : ICoreBankingAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FlexcubeAdapter> _logger;

    public string AdapterName => "Flexcube";

    public FlexcubeAdapter(IHttpClientFactory httpClientFactory, ILogger<FlexcubeAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CoreBankingExtractResult> ExtractReturnData(
        string moduleCode, string periodCode, CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        _logger.LogInformation("Flexcube: Extracting data for module {Module}, period {Period}", moduleCode, periodCode);

        var result = new CoreBankingExtractResult { Success = true };

        try
        {
            var client = _httpClientFactory.CreateClient("FlexcubeClient");
            client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));

            if (!string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(config.Password))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            // Flexcube SOAP/REST gateway: query GL balances
            var endpoint = $"/FCJRESTService/api/gl/balances/{Uri.EscapeDataString(periodCode)}";
            var response = await client.GetAsync(endpoint, ct);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Flexcube API returned {response.StatusCode}";
                return result;
            }

            var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(ct);
            if (data != null)
                result.FieldValues = data;

            result.Warnings.Add("Flexcube adapter: verify GL account mapping matches template fields");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flexcube extraction failed for {Module}/{Period}", moduleCode, periodCode);
            result.Success = false;
            result.ErrorMessage = $"Flexcube connection error: {ex.Message}";
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("FlexcubeClient");
            client.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/'));
            var response = await client.GetAsync("/FCJRESTService/api/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
