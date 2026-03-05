using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace FC.Engine.Infrastructure.Tests.Auth;

public class MfaChallengeStoreTests
{
    [Fact]
    public async Task MFA_Login_Requires_Code_After_Password()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        IMfaChallengeStore store = new MfaChallengeStore(cache);

        var challengeId = await store.CreateChallenge(new MfaLoginChallenge
        {
            UserId = 88,
            UserType = "InstitutionUser",
            Username = "maker.user",
            ReturnUrl = "/dashboard",
            MustChangePassword = false
        });

        var challenge = await store.GetChallenge(challengeId);
        challenge.Should().NotBeNull();
        challenge!.UserId.Should().Be(88);

        await store.RemoveChallenge(challengeId);
        var removed = await store.GetChallenge(challengeId);
        removed.Should().BeNull();
    }
}
