using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ApiClients;

public class SecApiClient : RegulatorApiClientBase
{
    public SecApiClient(
        HttpClient httpClient,
        IOptions<RegulatoryApiSettings> options,
        ILogger<SecApiClient> logger)
        : base(httpClient, options.Value.Sec.ApiKey, logger)
    {
    }

    public override string RegulatorCode => "SEC";
}
