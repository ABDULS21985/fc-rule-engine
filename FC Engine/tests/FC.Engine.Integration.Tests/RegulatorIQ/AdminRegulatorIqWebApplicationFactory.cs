extern alias AdminApp;

using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

public sealed class AdminRegulatorIqWebApplicationFactory : WebApplicationFactory<AdminApp::Program>
{
    private readonly RegulatorIqFixture _fixture;

    public AdminRegulatorIqWebApplicationFactory(RegulatorIqFixture fixture)
    {
        _fixture = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FcEngine"] = _fixture.ConnectionString,
                ["RabbitMQ:Enabled"] = "false",
                ["RegulatorPortal:DefaultTenantId"] = _fixture.CbnRegulatorTenantId.ToString("D"),
                ["RegulatorPortal:DefaultRegulatorCode"] = "CBN",
                ["RegulatorPortal:DefaultRegulatorName"] = "Central Bank of Nigeria"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                         .Where(x => x.ServiceType == typeof(IHostedService)
                            && x.ImplementationType?.Namespace?.StartsWith("FC.Engine.", StringComparison.Ordinal) == true)
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<DbContextOptions<MetadataDbContext>>();
            services.RemoveAll<MetadataDbContext>();

            services.AddDbContext<MetadataDbContext>((sp, options) =>
            {
                options.UseSqlServer(_fixture.ConnectionString, sql =>
                {
                    sql.CommandTimeout(30);
                    sql.EnableRetryOnFailure(3);
                });

                var interceptor = sp.GetRequiredService<TenantSessionContextInterceptor>();
                options.AddInterceptors(interceptor);
            });

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = AdminRegulatorIqTestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = AdminRegulatorIqTestAuthHandler.SchemeName;
                    options.DefaultScheme = AdminRegulatorIqTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, AdminRegulatorIqTestAuthHandler>(
                    AdminRegulatorIqTestAuthHandler.SchemeName,
                    _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient(
        string userId,
        Guid tenantId,
        string regulatorCode = "CBN",
        params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Add(AdminRegulatorIqTestAuthHandler.UserHeader, userId);
        client.DefaultRequestHeaders.Add(AdminRegulatorIqTestAuthHandler.TenantHeader, tenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(AdminRegulatorIqTestAuthHandler.RegulatorCodeHeader, regulatorCode);

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(AdminRegulatorIqTestAuthHandler.RolesHeader, string.Join(",", roles));
        }

        return client;
    }
}
