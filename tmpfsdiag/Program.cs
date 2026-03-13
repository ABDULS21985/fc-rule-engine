using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder()
    .SetBasePath("/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal")
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(builder => builder.AddSimpleConsole(options =>
{
    options.SingleLine = false;
    options.TimestampFormat = "HH:mm:ss ";
}));
services.AddInfrastructure(config);

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var foreSight = scope.ServiceProvider.GetRequiredService<IForeSightService>();
var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

Console.WriteLine($"Running ForeSight diagnostic for tenant {tenantId}...");

await foreSight.RunAllPredictionsAsync(tenantId, "FS_DIAG");
var dashboard = await foreSight.GetTenantDashboardAsync(tenantId);

Console.WriteLine($"Filing risks: {dashboard.FilingRisks.Count}");
Console.WriteLine($"Capital forecasts: {dashboard.CapitalForecasts.Count}");
Console.WriteLine($"Compliance forecast: {(dashboard.ComplianceForecast is null ? "none" : dashboard.ComplianceForecast.ProjectedScore.ToString("F2"))}");
Console.WriteLine($"Alerts: {dashboard.Alerts.Count}");
