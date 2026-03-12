using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FC.Engine.Admin.Tests.Infrastructure;

public sealed class PlatformIntelligenceApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                         .Where(x => x.ServiceType == typeof(IHostedService)
                    && x.ImplementationType?.Namespace?.StartsWith("FC.Engine.", StringComparison.Ordinal) == true)
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAdminAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAdminAuthHandler.SchemeName;
                    options.DefaultScheme = TestAdminAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAdminAuthHandler>(TestAdminAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient(params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Add(TestAdminAuthHandler.UserHeader, "integration-admin");
        client.DefaultRequestHeaders.Add(TestAdminAuthHandler.TenantHeader, DefaultTenantId.ToString());
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAdminAuthHandler.RolesHeader, string.Join(",", roles));
        }

        return client;
    }
}
