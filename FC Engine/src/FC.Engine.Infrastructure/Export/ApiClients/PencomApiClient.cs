using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ApiClients;

public class PencomApiClient : RegulatorApiClientBase
{
    public PencomApiClient(
        HttpClient httpClient,
        IOptions<RegulatoryApiSettings> options,
        ILogger<PencomApiClient> logger)
        : base(httpClient, options.Value.Pencom.ApiKey, logger)
    {
    }

    public override string RegulatorCode => "PENCOM";
}
