using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FC.Engine.Infrastructure.Metadata;

public sealed class MetadataDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MetadataDbContext>
{
    public MetadataDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("FcEngine")
            ?? "Server=localhost,1433;Database=FcEngine;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<MetadataDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sql =>
        {
            sql.CommandTimeout(30);
            sql.EnableRetryOnFailure(3);
            sql.MigrationsAssembly(typeof(MetadataDbContext).Assembly.FullName);
        });

        return new MetadataDbContext(optionsBuilder.Options);
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        var basePath = ResolveConfigurationBasePath();

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string ResolveConfigurationBasePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var appBaseDirectory = AppContext.BaseDirectory;

        var candidates = new[]
        {
            currentDirectory,
            Path.Combine(currentDirectory, "src", "FC.Engine.Migrator"),
            Path.Combine(currentDirectory, "..", "FC.Engine.Migrator"),
            appBaseDirectory,
            Path.Combine(appBaseDirectory, "..", "..", "..", "..", "FC.Engine.Migrator"),
            Path.Combine(appBaseDirectory, "..", "..", "..", "..", "..", "src", "FC.Engine.Migrator")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, "appsettings.json")))
            {
                return fullPath;
            }
        }

        return currentDirectory;
    }
}
