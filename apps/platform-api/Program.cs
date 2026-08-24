using FdeTutor.Api.Authentication;
using FdeTutor.Api.Content;
using FdeTutor.Api.Learning;
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
if (!Guid.TryParse(allowedTenantId, out _))
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
}
else
{
    throw new InvalidOperationException(
        "Authentication:Mode must be either 'Development' or 'Entra'.");
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LearnerAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.Claims
                .Where(claim =>
                    claim.Type == "scp" ||
                    claim.Type == "http://schemas.microsoft.com/identity/claims/scope")
                .SelectMany(claim => claim.Value.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
                .Contains("access_as_user", StringComparer.Ordinal));
    });
});

var persistenceProvider = builder.Configuration["Persistence:Provider"];
if (string.Equals(persistenceProvider, "InMemory", StringComparison.Ordinal))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "In-memory persistence is allowed only in Development or Testing.");
    }

    builder.Services.AddSingleton<ILearnerEventStore, InMemoryLearnerEventStore>();
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

app.UseExceptionHandler();
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
app.MapGet("/health/ready", (S083ContentProvider _) =>
        Results.Ok(new { status = "ready" }))
    .AllowAnonymous();
app.MapS083Endpoints();

app.Run();

public partial class Program;
