using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FdeTutor.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Mode", "Development");
        builder.UseSetting(
            "Authentication:AllowedTenantId",
            "11111111-1111-1111-1111-111111111111");
        builder.UseSetting("Persistence:Provider", "InMemory");
    }
}
