using System.Security.Claims;
using FC.Engine.Api.Middleware;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FC.Engine.Integration.Tests.Auth;

public class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task JWT_Bearer_Takes_Precedence_Over_ApiKey()
    {
        var tenantId = Guid.NewGuid();
        var jwtService = new FakeJwtTokenService(() =>
        {
            var claims = new[]
            {
                new Claim("sub", "77"),
                new Claim("tid", tenantId.ToString()),
                new Claim("perm", PermissionCatalog.SubmissionCreate)
            };
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        });

        var apiKeyService = new FakeApiKeyService(_ => Task.FromResult<ApiKeyValidationResult?>(new ApiKeyValidationResult
        {
            ApiKeyId = 10,
            TenantId = Guid.NewGuid(),
            Permissions = new[] { PermissionCatalog.SubmissionCreate }
        }));

        var middleware = CreateMiddleware(async _ => await Task.CompletedTask, apiKeyService, jwtService, "legacy-key");
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/submissions/1";
        context.Request.Headers.Authorization = "Bearer valid-token";
        context.Request.Headers["X-Api-Key"] = "regos_live_dummy";

        var nextCalled = false;
        middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, apiKeyService, jwtService, "legacy-key");

        await middleware.InvokeAsync(context, apiKeyService, jwtService);

        nextCalled.Should().BeTrue();
        apiKeyService.Calls.Should().Be(0);
        context.Items["TenantId"].Should().Be(tenantId);
        context.User.FindFirst("sub")!.Value.Should().Be("77");
    }

    [Fact]
    public async Task API_Key_Permissions_Are_Projected_Into_Claims()
    {
        var tenantId = Guid.NewGuid();
        var jwtService = new FakeJwtTokenService(() => throw new SecurityTokenException("No JWT"));
        var apiKeyService = new FakeApiKeyService(_ => Task.FromResult<ApiKeyValidationResult?>(new ApiKeyValidationResult
        {
            ApiKeyId = 501,
            TenantId = tenantId,
            Permissions = new[] { PermissionCatalog.SubmissionCreate, PermissionCatalog.ReportRead },
            RateLimitPerMinute = 100
        }));

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, apiKeyService, jwtService, "legacy-key");

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/submissions/1";
        context.Request.Headers["X-Api-Key"] = "regos_live_valid";

        await middleware.InvokeAsync(context, apiKeyService, jwtService);

        nextCalled.Should().BeTrue();
        context.Items["TenantId"].Should().Be(tenantId);
        context.User.FindAll("perm").Select(c => c.Value).Should().Contain(new[]
        {
            PermissionCatalog.SubmissionCreate,
            PermissionCatalog.ReportRead
        });
    }

    [Fact]
    public async Task Existing_X_Api_Key_Still_Works_For_Legacy_Static_Key()
    {
        var legacyTenantId = Guid.NewGuid();
        var jwtService = new FakeJwtTokenService(() => throw new SecurityTokenException("No JWT"));
        var apiKeyService = new FakeApiKeyService(_ => Task.FromResult<ApiKeyValidationResult?>(null));

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, apiKeyService, jwtService, "legacy-key");

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/submissions/1";
        context.Request.Headers["X-Api-Key"] = "legacy-key";
        context.Request.Headers["X-Tenant-Id"] = legacyTenantId.ToString();

        await middleware.InvokeAsync(context, apiKeyService, jwtService);

        nextCalled.Should().BeTrue();
        context.Items["TenantId"].Should().Be(legacyTenantId);
        context.User.Identity?.IsAuthenticated.Should().BeTrue();
        context.User.FindFirst("utype")?.Value.Should().Be("ApiKeyLegacy");
        context.User.FindAll("perm").Select(c => c.Value).Should().Contain(PermissionCatalog.SubmissionCreate);
    }

    [Fact]
    public async Task Legacy_Static_Key_Missing_Tenant_Header_Returns_403()
    {
        var jwtService = new FakeJwtTokenService(() => throw new SecurityTokenException("No JWT"));
        var apiKeyService = new FakeApiKeyService(_ => Task.FromResult<ApiKeyValidationResult?>(null));

        var middleware = CreateMiddleware(_ => Task.CompletedTask, apiKeyService, jwtService, "legacy-key");
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/submissions/1";
        context.Request.Headers["X-Api-Key"] = "legacy-key";

        await middleware.InvokeAsync(context, apiKeyService, jwtService);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static ApiKeyMiddleware CreateMiddleware(
        RequestDelegate next,
        IApiKeyService apiKeyService,
        IJwtTokenService jwtTokenService,
        string legacyKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey"] = legacyKey
            })
            .Build();

        return new ApiKeyMiddleware(
            next,
            configuration,
            NullLogger<ApiKeyMiddleware>.Instance);
    }

    private sealed class FakeApiKeyService : IApiKeyService
    {
        private readonly Func<string, Task<ApiKeyValidationResult?>> _resolver;
        public int Calls { get; private set; }

        public FakeApiKeyService(Func<string, Task<ApiKeyValidationResult?>> resolver)
        {
            _resolver = resolver;
        }

        public Task<ApiKeyCreateResult> CreateApiKey(Guid tenantId, int createdByUserId, CreateApiKeyRequest request, CancellationToken ct = default)
            => Task.FromResult(new ApiKeyCreateResult());

        public async Task<ApiKeyValidationResult?> ValidateApiKey(string rawKey, string? ipAddress, CancellationToken ct = default)
        {
            Calls++;
            return await _resolver(rawKey);
        }
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        private readonly Func<ClaimsPrincipal> _validate;

        public FakeJwtTokenService(Func<ClaimsPrincipal> validate)
        {
            _validate = validate;
        }

        public Task<TokenResponse> GenerateTokenPair(AuthenticatedUser user) => throw new NotSupportedException();
        public Task<TokenResponse> RefreshToken(string refreshToken, string? ipAddress) => throw new NotSupportedException();
        public Task RevokeToken(string refreshToken, string? ipAddress) => throw new NotSupportedException();
        public ClaimsPrincipal ValidateAccessToken(string accessToken) => _validate();
    }
}
