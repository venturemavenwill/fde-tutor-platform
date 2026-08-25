using FdeTutor.Api.Access;
using FdeTutor.Api.Authentication;
using FdeTutor.Api.Content;
using FdeTutor.Api.Learning;
using FdeTutor.Api.Projection;
using FdeTutor.Domain.Authorization;
using FdeTutor.Domain.Events;
using FdeTutor.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<S083ContentProvider>();
builder.Services.AddScoped<S083LearningService>();
builder.Services.AddScoped<LearnerAuthorizationContextFactory>();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("LearnerWeb", policy =>
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

var allowedTenantId = builder.Configuration["Authentication:AllowedTenantId"];
if (!Guid.TryParse(allowedTenantId, out var allowedTenant))
{
    throw new InvalidOperationException("Authentication:AllowedTenantId must be a UUID.");
}

var authenticationMode = builder.Configuration["Authentication:Mode"];
if (string.Equals(authenticationMode, "Development", StringComparison.Ordinal))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "Development authentication is allowed only in Development or Testing.");
    }

    builder.Services
        .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
}
else if (string.Equals(authenticationMode, "Entra", StringComparison.Ordinal))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    builder.Services.Configure<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme,
        options => options.TokenValidationParameters.RoleClaimType = "roles");
}
else
{
    throw new InvalidOperationException(
        "Authentication:Mode must be either 'Development' or 'Entra'.");
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PlatformPolicies.AuthenticatedAccess, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            PlatformClaims.HasDelegatedScope(context.User, "access_as_user"));
        policy.RequireAssertion(context =>
            PlatformClaims.HasApprovedSubject(context.User, allowedTenant));
    });
    options.AddPolicy(PlatformPolicies.LearnerAccess, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            PlatformClaims.HasDelegatedScope(context.User, "access_as_user"));
        policy.RequireAssertion(context =>
            PlatformClaims.HasApprovedSubject(context.User, allowedTenant));
        policy.RequireAssertion(context =>
            PlatformClaims.GetKnownRoles(context.User).Contains(PlatformRoles.Learner));
    });
    options.AddPolicy(PlatformPolicies.AdministratorAccess, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            PlatformClaims.HasDelegatedScope(context.User, "access_as_user"));
        policy.RequireAssertion(context =>
            PlatformClaims.HasApprovedSubject(context.User, allowedTenant));
        policy.RequireAssertion(context =>
            PlatformClaims.GetKnownRoles(context.User).Contains(PlatformRoles.Administrator));
    });
});

var persistenceProvider = builder.Configuration["Persistence:Provider"];
if (builder.Environment.IsEnvironment("TechnicalEvidence") &&
    (!string.Equals(authenticationMode, "Entra", StringComparison.Ordinal) ||
     !string.Equals(persistenceProvider, "Postgres", StringComparison.Ordinal) ||
     !builder.Configuration.GetValue("Deployment:EvidenceOnly", false)))
{
    throw new InvalidOperationException(
        "TechnicalEvidence requires Entra, PostgreSQL, and Deployment:EvidenceOnly=true.");
}

if (string.Equals(persistenceProvider, "InMemory", StringComparison.Ordinal))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "In-memory persistence is allowed only in Development or Testing.");
    }

    builder.Services.AddSingleton<ILearnerEventStore, InMemoryLearnerEventStore>();
    builder.Services.AddSingleton<IPlatformUserDirectory, InMemoryPlatformUserDirectory>();
}
else if (string.Equals(persistenceProvider, "Postgres", StringComparison.Ordinal))
{
    var connectionString = builder.Configuration.GetConnectionString("FdeTutor");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:FdeTutor is required for PostgreSQL persistence.");
    }

    builder.Services.AddDbContext<FdeTutorDbContext>(
        options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<ILearnerEventStore, PostgresLearnerEventStore>();
    builder.Services.AddScoped<IPlatformUserDirectory, PostgresPlatformUserDirectory>();
    builder.Services.AddScoped<SqlMigrationRunner>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<S083ProjectionBatchProcessor>();
    if (builder.Configuration.GetValue("Projection:Enabled", false))
    {
        builder.Services.AddHostedService<S083ProjectionHostedService>();
    }
}
else
{
    throw new InvalidOperationException(
        "Persistence:Provider must be either 'InMemory' or 'Postgres'.");
}

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(options =>
        {
            options.Filter = context =>
                !context.Request.Path.StartsWithSegments("/health");
        });
        tracing.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();

if (string.Equals(persistenceProvider, "Postgres", StringComparison.Ordinal) &&
    builder.Configuration.GetValue("Database:ApplyMigrations", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var migrationsRoot = builder.Configuration["Database:MigrationsRoot"];
    if (string.IsNullOrWhiteSpace(migrationsRoot))
    {
        throw new InvalidOperationException(
            "Database:MigrationsRoot is required when migrations are enabled.");
    }

    await scope.ServiceProvider
        .GetRequiredService<SqlMigrationRunner>()
        .ApplyAsync(migrationsRoot, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
if (allowedOrigins.Length > 0)
{
    app.UseCors("LearnerWeb");
}
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (
    S083ContentProvider _,
    IServiceProvider services,
    CancellationToken cancellationToken) =>
{
    if (string.Equals(persistenceProvider, "Postgres", StringComparison.Ordinal))
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FdeTutorDbContext>();
        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The canonical database is unavailable.");
        }
    }

    return Results.Ok(new { status = "ready" });
})
    .AllowAnonymous();
app.MapS083Endpoints();
app.MapAccessEndpoints();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
