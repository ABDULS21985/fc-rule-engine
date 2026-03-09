using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ApiClients;

public class NdicApiClient : RegulatorApiClientBase
{
    public NdicApiClient(
        HttpClient httpClient,
        IOptions<RegulatoryApiSettings> options,
        ILogger<NdicApiClient> logger)
        : base(httpClient, options.Value.Ndic.ApiKey, logger)
    {
    }

    public override string RegulatorCode => "NDIC";
}
