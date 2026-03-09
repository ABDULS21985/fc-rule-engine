using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ApiClients;

public class NaicomApiClient : RegulatorApiClientBase
{
    public NaicomApiClient(
        HttpClient httpClient,
        IOptions<RegulatoryApiSettings> options,
        ILogger<NaicomApiClient> logger)
        : base(httpClient, options.Value.Naicom.ApiKey, logger)
    {
    }

    public override string RegulatorCode => "NAICOM";
}
