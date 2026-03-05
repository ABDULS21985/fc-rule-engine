using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IPortalUserRepository> _userRepoMock;
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepoMock;
    private readonly Mock<IPasswordResetTokenRepository> _resetTokenRepoMock;
    private readonly Mock<IEntitlementService> _entitlementServiceMock;
    private readonly Mock<IPermissionService> _permissionServiceMock;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _userRepoMock = new Mock<IPortalUserRepository>();
        _loginAttemptRepoMock = new Mock<ILoginAttemptRepository>();
        _resetTokenRepoMock = new Mock<IPasswordResetTokenRepository>();
        _entitlementServiceMock = new Mock<IEntitlementService>();
        _permissionServiceMock = new Mock<IPermissionService>();

        _loginAttemptRepoMock
            .Setup(r => r.Create(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _permissionServiceMock
            .Setup(s => s.GetPermissions(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _sut = new AuthService(
            _userRepoMock.Object,
            _loginAttemptRepoMock.Object,
            _resetTokenRepoMock.Object,
            _entitlementServiceMock.Object,
            _permissionServiceMock.Object);
    }

    private PortalUser CreateTestUser(string username = "testuser", string password = "SecureP@ss1!",
        PortalRole role = PortalRole.Admin, bool isActive = true, int failedAttempts = 0,
        DateTime? lockoutEnd = null)
    {
        return new PortalUser
        {
            Id = 1,
            Username = username,
            DisplayName = "Test User",
            Email = $"{username}@fcengine.local",
            PasswordHash = AuthService.HashPassword(password),
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            LastLoginAt = null,
            FailedLoginAttempts = failedAttempts,
            LockoutEnd = lockoutEnd
        };
    }

    private void SetupUserLookup(PortalUser? user, string? username = null)
    {
        var name = username ?? user?.Username ?? "testuser";
        _userRepoMock
            .Setup(r => r.GetByUsername(name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock
            .Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ═══════════════════════════════════════════════════
    // ValidateLogin — Successful authentication
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task ValidateLogin_ValidCredentials_ReturnsUserWithNoError()
    {
        var password = "SecureP@ss1!";
        var user = CreateTestUser(password: password);
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", password);

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Username.Should().Be("testuser");
        errorCode.Should().BeNull();
    }

    [Fact]
    public async Task ValidateLogin_ValidCredentials_UpdatesLastLoginAt()
    {
        var password = "SecureP@ss1!";
        var user = CreateTestUser(password: password);
        SetupUserLookup(user);

        var (result, _) = await _sut.ValidateLogin("testuser", password);

        result!.LastLoginAt.Should().NotBeNull();
        result.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        _userRepoMock.Verify(r => r.Update(It.Is<PortalUser>(u => u.LastLoginAt != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateLogin_ValidCredentials_ResetsFailedAttemptsCounter()
    {
        var password = "SecureP@ss1!";
        var user = CreateTestUser(password: password, failedAttempts: 3);
        SetupUserLookup(user);

        var (result, _) = await _sut.ValidateLogin("testuser", password);

        result.Should().NotBeNull();
        result!.FailedLoginAttempts.Should().Be(0);
        result.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task ValidateLogin_ValidCredentials_RecordsSuccessfulAttempt()
    {
        var password = "SecureP@ss1!";
        var user = CreateTestUser(password: password);
        SetupUserLookup(user);

        await _sut.ValidateLogin("testuser", password, "192.168.1.1");

        _loginAttemptRepoMock.Verify(r => r.Create(
            It.Is<LoginAttempt>(a =>
                a.Username == "testuser" &&
                a.Succeeded == true &&
                a.IpAddress == "192.168.1.1" &&
                a.FailureReason == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═══════════════════════════════════════════════════
    // ValidateLogin — Failed authentication
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task ValidateLogin_NonExistentUser_ReturnsInvalidError()
    {
        _userRepoMock
            .Setup(r => r.GetByUsername("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortalUser?)null);

        var (result, errorCode) = await _sut.ValidateLogin("ghost", "anypassword");

        result.Should().BeNull();
        errorCode.Should().Be("invalid");
    }

    [Fact]
    public async Task ValidateLogin_NonExistentUser_RecordsAttemptWithUserNotFoundReason()
    {
        _userRepoMock
            .Setup(r => r.GetByUsername("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortalUser?)null);

        await _sut.ValidateLogin("ghost", "anypassword", "10.0.0.1");

        _loginAttemptRepoMock.Verify(r => r.Create(
            It.Is<LoginAttempt>(a =>
                a.Username == "ghost" &&
                a.Succeeded == false &&
                a.FailureReason == "user_not_found"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateLogin_InactiveUser_ReturnsDeniedError()
    {
        var user = CreateTestUser(isActive: false);
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", "SecureP@ss1!");

        result.Should().BeNull();
        errorCode.Should().Be("denied");
    }

    [Fact]
    public async Task ValidateLogin_WrongPassword_ReturnsInvalidError()
    {
        var user = CreateTestUser(password: "CorrectP@ss1!");
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", "WrongPassword!");

        result.Should().BeNull();
        errorCode.Should().Be("invalid");
    }

    [Fact]
    public async Task ValidateLogin_WrongPassword_IncrementsFailedAttempts()
    {
        var user = CreateTestUser(password: "CorrectP@ss1!", failedAttempts: 0);
        SetupUserLookup(user);

        await _sut.ValidateLogin("testuser", "WrongPassword!");

        user.FailedLoginAttempts.Should().Be(1);
        _userRepoMock.Verify(r => r.Update(
            It.Is<PortalUser>(u => u.FailedLoginAttempts == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateLogin_WrongPassword_RecordsAttemptWithBadPasswordReason()
    {
        var user = CreateTestUser(password: "CorrectP@ss1!");
        SetupUserLookup(user);

        await _sut.ValidateLogin("testuser", "WrongPassword!", "172.16.0.5");

        _loginAttemptRepoMock.Verify(r => r.Create(
            It.Is<LoginAttempt>(a =>
                a.Username == "testuser" &&
                a.Succeeded == false &&
                a.FailureReason == "bad_password" &&
                a.IpAddress == "172.16.0.5"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═══════════════════════════════════════════════════
    // ValidateLogin — Lockout / rate-limiting
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task ValidateLogin_FifthFailedAttempt_TriggersLockout()
    {
        var user = CreateTestUser(password: "CorrectP@ss1!", failedAttempts: 4);
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", "WrongPassword!");

        result.Should().BeNull();
        errorCode.Should().Be("locked");
        user.FailedLoginAttempts.Should().Be(5);
        user.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ValidateLogin_AlreadyLockedAccount_ReturnsLockedError()
    {
        var user = CreateTestUser(password: "CorrectP@ss1!",
            failedAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(10));
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", "CorrectP@ss1!");

        result.Should().BeNull();
        errorCode.Should().Be("locked");
    }

    [Fact]
    public async Task ValidateLogin_LockedAccountWithCorrectPassword_StillReturnsLocked()
    {
        var password = "CorrectP@ss1!";
        var user = CreateTestUser(password: password,
            failedAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(10));
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", password);

        result.Should().BeNull();
        errorCode.Should().Be("locked");
    }

    [Fact]
    public async Task ValidateLogin_ExpiredLockout_AllowsLogin()
    {
        var password = "CorrectP@ss1!";
        var user = CreateTestUser(password: password,
            failedAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(-1));
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", password);

        result.Should().NotBeNull();
        errorCode.Should().BeNull();
        result!.FailedLoginAttempts.Should().Be(0);
        result.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task ValidateLogin_ExpiredLockoutWrongPassword_RelocksImmediately()
    {
        // After expired lockout, wrong password still increments counter.
        // Since counter was already 5, going to 6 triggers re-lock immediately.
        var user = CreateTestUser(password: "CorrectP@ss1!",
            failedAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(-1));
        SetupUserLookup(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", "StillWrong!");

        result.Should().BeNull();
        errorCode.Should().Be("locked");
        user.FailedLoginAttempts.Should().Be(6);
        user.LockoutEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateLogin_LockedAccount_RecordsAttemptWithLockedReason()
    {
        var user = CreateTestUser(
            failedAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(10));
        SetupUserLookup(user);

        await _sut.ValidateLogin("testuser", "anything");

        _loginAttemptRepoMock.Verify(r => r.Create(
            It.Is<LoginAttempt>(a => a.FailureReason == "locked"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateLogin_ProgressiveFailures_LocksOnFifthAttempt()
    {
        var password = "CorrectP@ss1!";
        var user = CreateTestUser(password: password, failedAttempts: 0);
        SetupUserLookup(user);

        for (int i = 1; i <= 4; i++)
        {
            var (r, err) = await _sut.ValidateLogin("testuser", "wrong");
            r.Should().BeNull();
            err.Should().Be("invalid", $"attempt {i} of 5 should be 'invalid'");
            user.FailedLoginAttempts.Should().Be(i);
        }

        var (result, errorCode) = await _sut.ValidateLogin("testuser", "wrong");
        result.Should().BeNull();
        errorCode.Should().Be("locked");
        user.FailedLoginAttempts.Should().Be(5);
        user.LockoutEnd.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════
    // CreateUser
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task CreateUser_ValidData_CreatesUserWithHashedPassword()
    {
        _userRepoMock
            .Setup(r => r.UsernameExists("newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        PortalUser? captured = null;
        _userRepoMock
            .Setup(r => r.Create(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()))
            .Callback<PortalUser, CancellationToken>((u, _) => captured = u)
            .ReturnsAsync((PortalUser u, CancellationToken _) => u);

        var result = await _sut.CreateUser("newuser", "New User", "new@fcengine.local", "NewPass1!", PortalRole.Approver);

        captured.Should().NotBeNull();
        captured!.Username.Should().Be("newuser");
        captured.DisplayName.Should().Be("New User");
        captured.Email.Should().Be("new@fcengine.local");
        captured.Role.Should().Be(PortalRole.Approver);
        captured.IsActive.Should().BeTrue();
        captured.PasswordHash.Should().NotBe("NewPass1!");
        captured.PasswordHash.Should().Contain(":");
        captured.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_ThrowsInvalidOperationException()
    {
        _userRepoMock
            .Setup(r => r.UsernameExists("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _sut.CreateUser("existing", "Name", "e@e.com", "P@ss1!", PortalRole.Viewer);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'existing'*");

        _userRepoMock.Verify(r => r.Create(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateUser_PasswordRoundtrip_CanBeValidatedViaLogin()
    {
        var password = "R0undTr!pP@ss";
        PortalUser? createdUser = null;

        _userRepoMock
            .Setup(r => r.UsernameExists("roundtrip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _userRepoMock
            .Setup(r => r.Create(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()))
            .Callback<PortalUser, CancellationToken>((u, _) => createdUser = u)
            .ReturnsAsync((PortalUser u, CancellationToken _) => u);

        await _sut.CreateUser("roundtrip", "Roundtrip", "rt@test.com", password, PortalRole.Admin);

        _userRepoMock
            .Setup(r => r.GetByUsername("roundtrip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);
        _userRepoMock
            .Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (result, errorCode) = await _sut.ValidateLogin("roundtrip", password);

        result.Should().NotBeNull("created user should be able to log in with the same password");
        errorCode.Should().BeNull();
        result!.Username.Should().Be("roundtrip");
    }

    // ═══════════════════════════════════════════════════
    // ChangePassword
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task ChangePassword_ValidUser_UpdatesHash()
    {
        var oldHash = AuthService.HashPassword("OldPass1!");
        var user = new PortalUser
        {
            Id = 10, Username = "pwduser", DisplayName = "Pwd User",
            Email = "pwd@test.com", PasswordHash = oldHash,
            Role = PortalRole.Admin, IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            FailedLoginAttempts = 3, LockoutEnd = DateTime.UtcNow.AddMinutes(5)
        };

        _userRepoMock.Setup(r => r.GetById(10, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ChangePassword(10, "NewPass1!");

        user.PasswordHash.Should().NotBe(oldHash);
        user.PasswordHash.Should().Contain(":");
    }

    [Fact]
    public async Task ChangePassword_ResetsLockoutCounters()
    {
        var user = new PortalUser
        {
            Id = 10, Username = "lockeduser", DisplayName = "Locked User",
            Email = "locked@test.com", PasswordHash = AuthService.HashPassword("OldPass1!"),
            Role = PortalRole.Admin, IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            FailedLoginAttempts = 5, LockoutEnd = DateTime.UtcNow.AddMinutes(10)
        };

        _userRepoMock.Setup(r => r.GetById(10, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ChangePassword(10, "NewPass1!");

        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task ChangePassword_NonExistentUser_Throws()
    {
        _userRepoMock.Setup(r => r.GetById(999, It.IsAny<CancellationToken>())).ReturnsAsync((PortalUser?)null);

        var act = () => _sut.ChangePassword(999, "NewPass1!");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*User not found*");
    }

    [Fact]
    public async Task ChangePassword_CanLoginWithNewPassword()
    {
        var user = new PortalUser
        {
            Id = 10, Username = "pwdchange", DisplayName = "Pwd Change",
            Email = "pwdc@test.com", PasswordHash = AuthService.HashPassword("OldPass1!"),
            Role = PortalRole.Viewer, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-5)
        };

        _userRepoMock.Setup(r => r.GetById(10, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var newPassword = "BrandNewP@ss1!";
        await _sut.ChangePassword(10, newPassword);

        _userRepoMock.Setup(r => r.GetByUsername("pwdchange", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var (result, errorCode) = await _sut.ValidateLogin("pwdchange", newPassword);

        result.Should().NotBeNull();
        errorCode.Should().BeNull();
    }

    [Fact]
    public async Task ChangePassword_OldPasswordNoLongerWorks()
    {
        var oldPassword = "OldPass1!";
        var user = new PortalUser
        {
            Id = 10, Username = "pwdchange2", DisplayName = "Pwd Change2",
            Email = "pwdc2@test.com", PasswordHash = AuthService.HashPassword(oldPassword),
            Role = PortalRole.Viewer, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-5)
        };

        _userRepoMock.Setup(r => r.GetById(10, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ChangePassword(10, "BrandNewP@ss1!");

        _userRepoMock.Setup(r => r.GetByUsername("pwdchange2", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var (result, errorCode) = await _sut.ValidateLogin("pwdchange2", oldPassword);

        result.Should().BeNull();
        errorCode.Should().Be("invalid");
    }

    // ═══════════════════════════════════════════════════
    // HashPassword (static)
    // ═══════════════════════════════════════════════════

    [Fact]
    public void HashPassword_SamePassword_ProducesDifferentHashes()
    {
        var hash1 = AuthService.HashPassword("SameP@ss!");
        var hash2 = AuthService.HashPassword("SameP@ss!");

        hash1.Should().NotBe(hash2, "each hash uses a unique random salt");
        hash1.Split(':')[0].Should().NotBe(hash2.Split(':')[0], "salts should differ");
    }

    [Fact]
    public void HashPassword_Format_IsSaltColonHashInBase64()
    {
        var result = AuthService.HashPassword("TestP@ss!");

        var parts = result.Split(':');
        parts.Should().HaveCount(2);

        var salt = Convert.FromBase64String(parts[0]);
        salt.Should().HaveCount(16, "salt should be 128 bits");

        var hash = Convert.FromBase64String(parts[1]);
        hash.Should().HaveCount(32, "hash should be 256 bits");
    }

    // ═══════════════════════════════════════════════════
    // GeneratePasswordResetToken
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task GeneratePasswordResetToken_ValidEmail_ReturnsToken()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmail("testuser@fcengine.local", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _resetTokenRepoMock.Setup(r => r.InvalidateAllForUser(user.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _resetTokenRepoMock.Setup(r => r.Create(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var token = await _sut.GeneratePasswordResetToken("testuser@fcengine.local");

        token.Should().NotBeNullOrWhiteSpace();
        token!.Length.Should().BeGreaterThan(20, "token should be a long secure string");
    }

    [Fact]
    public async Task GeneratePasswordResetToken_InvalidatesExistingTokens()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmail("testuser@fcengine.local", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _resetTokenRepoMock.Setup(r => r.InvalidateAllForUser(user.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _resetTokenRepoMock.Setup(r => r.Create(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.GeneratePasswordResetToken("testuser@fcengine.local");

        _resetTokenRepoMock.Verify(r => r.InvalidateAllForUser(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GeneratePasswordResetToken_StoresTokenWithOneHourExpiry()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmail("testuser@fcengine.local", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _resetTokenRepoMock.Setup(r => r.InvalidateAllForUser(user.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        PasswordResetToken? captured = null;
        _resetTokenRepoMock
            .Setup(r => r.Create(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
            .Callback<PasswordResetToken, CancellationToken>((t, _) => captured = t)
            .Returns(Task.CompletedTask);

        await _sut.GeneratePasswordResetToken("testuser@fcengine.local");

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(user.Id);
        captured.IsUsed.Should().BeFalse();
        captured.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
        captured.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GeneratePasswordResetToken_NonExistentEmail_ReturnsNull()
    {
        _userRepoMock.Setup(r => r.GetByEmail("ghost@nowhere.com", It.IsAny<CancellationToken>())).ReturnsAsync((PortalUser?)null);

        var token = await _sut.GeneratePasswordResetToken("ghost@nowhere.com");

        token.Should().BeNull();
        _resetTokenRepoMock.Verify(r => r.Create(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GeneratePasswordResetToken_InactiveUser_ReturnsNull()
    {
        var user = CreateTestUser(isActive: false);
        _userRepoMock.Setup(r => r.GetByEmail("testuser@fcengine.local", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var token = await _sut.GeneratePasswordResetToken("testuser@fcengine.local");

        token.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════
    // ValidateResetToken
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task ValidateResetToken_ValidToken_ReturnsEmail()
    {
        var user = CreateTestUser();
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "valid-token-123",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("valid-token-123", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);

        var email = await _sut.ValidateResetToken("valid-token-123");

        email.Should().Be("testuser@fcengine.local");
    }

    [Fact]
    public async Task ValidateResetToken_ExpiredToken_ReturnsNull()
    {
        var user = CreateTestUser();
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddHours(-2), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("expired-token", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);

        var email = await _sut.ValidateResetToken("expired-token");

        email.Should().BeNull();
    }

    [Fact]
    public async Task ValidateResetToken_AlreadyUsedToken_ReturnsNull()
    {
        var user = CreateTestUser();
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "used-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = true,
            UsedAt = DateTime.UtcNow.AddMinutes(-2),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("used-token", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);

        var email = await _sut.ValidateResetToken("used-token");

        email.Should().BeNull();
    }

    [Fact]
    public async Task ValidateResetToken_NonExistentToken_ReturnsNull()
    {
        _resetTokenRepoMock.Setup(r => r.GetByToken("fake-token", It.IsAny<CancellationToken>())).ReturnsAsync((PasswordResetToken?)null);

        var email = await _sut.ValidateResetToken("fake-token");

        email.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════
    // ResetPasswordWithToken
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task ResetPasswordWithToken_ValidToken_ChangesPasswordAndReturnsTrue()
    {
        var user = CreateTestUser(password: "OldPass1!", failedAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(10));
        var oldHash = user.PasswordHash;
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "reset-token-123",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("reset-token-123", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _resetTokenRepoMock.Setup(r => r.Update(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var success = await _sut.ResetPasswordWithToken("reset-token-123", "NewResetP@ss1!");

        success.Should().BeTrue();
        user.PasswordHash.Should().NotBe(oldHash);
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task ResetPasswordWithToken_MarksTokenAsUsed()
    {
        var user = CreateTestUser();
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "mark-used-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("mark-used-token", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _resetTokenRepoMock.Setup(r => r.Update(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ResetPasswordWithToken("mark-used-token", "NewP@ss1!");

        resetToken.IsUsed.Should().BeTrue();
        resetToken.UsedAt.Should().NotBeNull();
        resetToken.UsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ResetPasswordWithToken_CanLoginWithNewPassword()
    {
        var user = CreateTestUser(password: "OldPass1!");
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "login-after-reset",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("login-after-reset", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _resetTokenRepoMock.Setup(r => r.Update(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var newPassword = "AfterResetP@ss1!";
        await _sut.ResetPasswordWithToken("login-after-reset", newPassword);

        _userRepoMock.Setup(r => r.GetByUsername("testuser", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", newPassword);

        result.Should().NotBeNull();
        errorCode.Should().BeNull();
    }

    [Fact]
    public async Task ResetPasswordWithToken_OldPasswordStopsWorking()
    {
        var oldPassword = "OldPass1!";
        var user = CreateTestUser(password: oldPassword);
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "old-pwd-test",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("old-pwd-test", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _resetTokenRepoMock.Setup(r => r.Update(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ResetPasswordWithToken("old-pwd-test", "BrandNew1!");

        _userRepoMock.Setup(r => r.GetByUsername("testuser", It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var (result, errorCode) = await _sut.ValidateLogin("testuser", oldPassword);

        result.Should().BeNull();
        errorCode.Should().Be("invalid");
    }

    [Fact]
    public async Task ResetPasswordWithToken_ExpiredToken_ReturnsFalse()
    {
        var user = CreateTestUser();
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "expired-reset",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10), IsUsed = false,
            CreatedAt = DateTime.UtcNow.AddHours(-2), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("expired-reset", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);

        var success = await _sut.ResetPasswordWithToken("expired-reset", "NewP@ss1!");

        success.Should().BeFalse();
        _userRepoMock.Verify(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordWithToken_AlreadyUsedToken_ReturnsFalse()
    {
        var user = CreateTestUser();
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = user.Id, Token = "already-used",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = true,
            UsedAt = DateTime.UtcNow.AddMinutes(-3),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10), User = user
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("already-used", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);

        var success = await _sut.ResetPasswordWithToken("already-used", "NewP@ss1!");

        success.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPasswordWithToken_NonExistentToken_ReturnsFalse()
    {
        _resetTokenRepoMock.Setup(r => r.GetByToken("no-such-token", It.IsAny<CancellationToken>())).ReturnsAsync((PasswordResetToken?)null);

        var success = await _sut.ResetPasswordWithToken("no-such-token", "NewP@ss1!");

        success.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════
    // Full E2E lifecycle
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_CreateUser_Login_Lockout_ResetPassword_LoginAgain()
    {
        // Step 1: Create user
        var password = "Initial1!";
        PortalUser? createdUser = null;

        _userRepoMock.Setup(r => r.UsernameExists("lifecycle", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _userRepoMock
            .Setup(r => r.Create(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>()))
            .Callback<PortalUser, CancellationToken>((u, _) => createdUser = u)
            .ReturnsAsync((PortalUser u, CancellationToken _) => u);

        await _sut.CreateUser("lifecycle", "Lifecycle User", "lc@test.com", password, PortalRole.Admin);
        createdUser.Should().NotBeNull();

        // Step 2: Login successfully
        _userRepoMock.Setup(r => r.GetByUsername("lifecycle", It.IsAny<CancellationToken>())).ReturnsAsync(createdUser);
        _userRepoMock.Setup(r => r.Update(It.IsAny<PortalUser>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var (user, err) = await _sut.ValidateLogin("lifecycle", password);
        user.Should().NotBeNull();
        err.Should().BeNull();

        // Step 3: Fail 5 times → lockout
        for (int i = 0; i < 5; i++)
            await _sut.ValidateLogin("lifecycle", "wrong!");

        createdUser!.FailedLoginAttempts.Should().Be(5);
        createdUser.LockoutEnd.Should().NotBeNull();

        // Step 4: Correct password rejected while locked
        var (lockedResult, lockedErr) = await _sut.ValidateLogin("lifecycle", password);
        lockedResult.Should().BeNull();
        lockedErr.Should().Be("locked");

        // Step 5: Reset password via token
        var resetToken = new PasswordResetToken
        {
            Id = 1, UserId = createdUser.Id, Token = "lifecycle-reset",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsUsed = false,
            CreatedAt = DateTime.UtcNow, User = createdUser
        };

        _resetTokenRepoMock.Setup(r => r.GetByToken("lifecycle-reset", It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);
        _resetTokenRepoMock.Setup(r => r.Update(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var newPassword = "AfterReset1!";
        var resetSuccess = await _sut.ResetPasswordWithToken("lifecycle-reset", newPassword);
        resetSuccess.Should().BeTrue();

        // Step 6: Lockout cleared
        createdUser.FailedLoginAttempts.Should().Be(0);
        createdUser.LockoutEnd.Should().BeNull();

        // Step 7: Login with new password
        var (finalUser, finalErr) = await _sut.ValidateLogin("lifecycle", newPassword);
        finalUser.Should().NotBeNull();
        finalErr.Should().BeNull();

        // Step 8: Old password rejected
        var (oldPwdResult, oldPwdErr) = await _sut.ValidateLogin("lifecycle", password);
        oldPwdResult.Should().BeNull();
        oldPwdErr.Should().Be("invalid");
    }
}
