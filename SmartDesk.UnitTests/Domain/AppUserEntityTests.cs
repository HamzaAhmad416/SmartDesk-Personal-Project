using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using Xunit;

namespace SmartDesk.UnitTests.Domain;

public class AppUserEntityTests
{
    // ── Creation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidInputs_ReturnsUserWithUserRole()
    {
        var user = AppUser.Create("hamza@test.com", "Hamza Ahmad");

        Assert.Equal("hamza@test.com", user.Email);
        Assert.Equal("Hamza Ahmad", user.DisplayName);
        Assert.Equal(UserRole.User, user.Role);
        Assert.True(user.IsActive);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public void Create_EmailIsNormalisedToLowercase()
    {
        // Business rule: emails are always stored lowercase
        var user = AppUser.Create("HAMZA@TEST.COM", "Hamza");

        Assert.Equal("hamza@test.com", user.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyEmail_ThrowsArgumentException(string email)
    {
        Assert.Throws<ArgumentException>(() =>
            AppUser.Create(email, "Hamza Ahmad"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyDisplayName_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            AppUser.Create("hamza@test.com", name));
    }

    // ── Role Promotion ────────────────────────────────────────────────────────

    [Fact]
    public void Promote_ChangesRoleAndStampsUpdatedAt()
    {
        var user = AppUser.Create("hamza@test.com", "Hamza Ahmad");

        user.Promote(UserRole.Agent);

        Assert.Equal(UserRole.Agent, user.Role);
        Assert.NotNull(user.UpdatedAt);
    }

    [Fact]
    public void Promote_ToAdmin_SetsAdminRole()
    {
        var user = AppUser.Create("hamza@test.com", "Hamza Ahmad");

        user.Promote(UserRole.Admin);

        Assert.Equal(UserRole.Admin, user.Role);
    }

    // ── Deactivation ──────────────────────────────────────────────────────────

    [Fact]
    public void Deactivate_SetsIsActiveToFalse()
    {
        var user = AppUser.Create("hamza@test.com", "Hamza Ahmad");

        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.NotNull(user.UpdatedAt);
    }

    // ── Azure AD ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithAzureAdObjectId_StoresId()
    {
        const string azureId = "azure-object-id-123";

        var user = AppUser.Create("hamza@test.com", "Hamza Ahmad",
            azureAdObjectId: azureId);

        Assert.Equal(azureId, user.AzureAdObjectId);
    }
}
