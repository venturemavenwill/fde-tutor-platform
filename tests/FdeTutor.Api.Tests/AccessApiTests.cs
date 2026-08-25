using System.Net;
using System.Net.Http.Json;
using FdeTutor.Contracts.Api;

namespace FdeTutor.Api.Tests;

public sealed class AccessApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AccessConsoleExposesDurableSubjectAndSixRoleMatrix()
    {
        using var client = CreateClient("Learner,Administrator");

        var access = await client.GetFromJsonAsync<AccessConsoleResponse>(
            "/api/v1/access",
            CancellationToken.None);

        Assert.NotNull(access);
        Assert.Equal("g02-phase1-1", access.MatrixVersion);
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            access.CurrentUser.TenantId);
        Assert.Equal(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            access.CurrentUser.ObjectId);
        Assert.Equal(6, access.Roles.Count);
        Assert.Contains("Learner", access.CurrentUser.Roles);
        Assert.Contains("Administrator", access.CurrentUser.Roles);
        Assert.Contains(
            "directory.read-tenant",
            access.CurrentUser.EffectiveCapabilities);
        Assert.False(access.UserManagement.RoleMutationAvailable);
        Assert.False(access.UserManagement.EnrolmentAvailable);
    }

    [Fact]
    public async Task AdministratorDirectoryIsTenantScopedAndContainsNoEmail()
    {
        using var administrator = CreateClient("Learner,Administrator");
        await administrator.GetAsync("/api/v1/access", CancellationToken.None);

        var response = await administrator.GetAsync(
            "/api/v1/access/users",
            CancellationToken.None);
        var payload = await response.Content.ReadAsStringAsync(
            CancellationToken.None);
        var users = await response.Content.ReadFromJsonAsync<ObservedUserResponse[]>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = Assert.Single(users!);
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            user.TenantId);
        Assert.DoesNotContain("email", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LearnerCannotListTheTenantUserDirectory()
    {
        using var learner = CreateClient();

        var response = await learner.GetAsync(
            "/api/v1/access/users",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewerCanInspectAccessButCannotEnterS083()
    {
        using var reviewer = CreateClient("Reviewer");

        var access = await reviewer.GetAsync(
            "/api/v1/access",
            CancellationToken.None);
        var content = await reviewer.GetAsync(
            "/api/v1/s083/content",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, content.StatusCode);
    }

    [Fact]
    public async Task UnknownDevelopmentRoleIsRejected()
    {
        using var client = CreateClient("TenantOwner");

        var response = await client.GetAsync(
            "/api/v1/access",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnapprovedTenantCannotInspectAccessConfiguration()
    {
        using var client = CreateClient(
            "Learner,Administrator",
            tenantId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var response = await client.GetAsync(
            "/api/v1/access",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(
        string? roles = null,
        string tenantId = "11111111-1111-1111-1111-111111111111",
        string objectId = "22222222-2222-2222-2222-222222222222")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fde-Tenant-Id", tenantId);
        client.DefaultRequestHeaders.Add("X-Fde-Object-Id", objectId);
        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add("X-Fde-Roles", roles);
        }

        return client;
    }
}
