using System.Data;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Caching;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Metadata.Repositories;
using FC.Engine.Infrastructure.Persistence;
using FC.Engine.Infrastructure.Persistence.Repositories;
using FC.Engine.Infrastructure.Validation;
using FC.Engine.Infrastructure.Xml;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FC.Engine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FcEngine")
            ?? throw new InvalidOperationException("Connection string 'FcEngine' not found");

        // EF Core for metadata + operational tables (pooled for performance)
        services.AddDbContextPool<MetadataDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(30);
                sql.EnableRetryOnFailure(3);
            }));

        // Dapper connection for dynamic data tables
        services.AddScoped<IDbConnection>(_ => new SqlConnection(connectionString));

        // Repositories
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IFormulaRepository, FormulaRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IGenericDataRepository, GenericDataRepository>();
        services.AddScoped<IPortalUserRepository, PortalUserRepository>();

        // Dynamic SQL
        services.AddSingleton<DynamicSqlBuilder>();

        // DDL Engine
        services.AddScoped<IDdlEngine, DdlEngine>();
        services.AddScoped<IDdlMigrationExecutor, DdlMigrationExecutor>();
        services.AddSingleton<ISqlTypeMapper, SqlTypeMapper>();

        // XML
        services.AddScoped<IGenericXmlParser, GenericXmlParser>();
        services.AddScoped<IXsdGenerator, XsdGenerator>();

        // Caching — singleton so the in-memory ConcurrentDictionary lives across requests
        services.AddSingleton<ITemplateMetadataCache, TemplateMetadataCache>();
        services.AddHostedService<CacheWarmupService>();

        // Audit
        services.AddScoped<IAuditLogger, AuditLogger>();

        // Validation
        services.AddSingleton<ExpressionParser>();
        services.AddSingleton<ExpressionTokenizer>();
        services.AddScoped<IFormulaEvaluator, FormulaEvaluator>();
        services.AddScoped<ICrossSheetValidator, CrossSheetValidator>();
        services.AddScoped<IBusinessRuleEvaluator, BusinessRuleEvaluator>();

        return services;
    }
}
