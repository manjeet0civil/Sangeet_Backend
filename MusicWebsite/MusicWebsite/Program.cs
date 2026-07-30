using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MusicWebsite.Application;
using MusicWebsite.Extensions;
using MusicWebsite.Infrastructure;
using MusicWebsite.Infrastructure.Security;
using MusicWebsite.Middleware;

// Read .env into the environment BEFORE the builder, so BACKEND_PORT & friends reach IConfiguration.
var envFile = DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// ----- MVC / Controllers -----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ----- HTTP endpoint: host + port come from .env, so they can be changed without touching code -----
// BACKEND_HOST=0.0.0.0 listens on every network interface (localhost + LAN IP). Use 127.0.0.1 to
// keep the API private to this machine. An explicit ASPNETCORE_URLS still wins, so hosts like IIS
// or Azure that set it themselves keep working.
//
// PORT takes priority over BACKEND_PORT: container platforms (Render, Railway, Heroku, Fly) assign
// the port and inject it as PORT. Ignoring it means the platform's health check hits a closed port
// and the deploy is marked failed. When PORT is present we also force 0.0.0.0 — binding to
// 127.0.0.1 inside a container makes the app unreachable from outside it.
var platformPort = builder.Configuration["PORT"];
var isPlatformHosted = !string.IsNullOrWhiteSpace(platformPort);

var backendHost = isPlatformHosted ? "0.0.0.0" : builder.Configuration["BACKEND_HOST"] ?? "0.0.0.0";
var backendPort = int.TryParse(platformPort, out var assignedPort) ? assignedPort
                : int.TryParse(builder.Configuration["BACKEND_PORT"], out var configuredPort) ? configuredPort
                : 5000;

if (string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls($"http://{backendHost}:{backendPort}");
}


// ----- Swagger with JWT bearer support -----
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "My Music API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token (without the 'Bearer ' prefix).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// ----- Fail fast, and say exactly WHY -----
// appsettings.json is gitignored, so a container built from the repo does not contain it and every
// secret must arrive as an environment variable. Without this check the app dies on whichever
// setting it happens to touch first, with a message that doesn't say what to fix. This reports
// everything that's missing at once, in copy-pasteable form.
StartupConfigCheck.Validate(builder.Configuration);

// ----- Application & Infrastructure layers -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ----- Authentication (JWT) -----
var jwt = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
          ?? throw new InvalidOperationException("Jwt settings are not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep JWT claims exactly as we emit them (don't remap short names like "role"/"email" to
        // long URIs). Without this, the "role" claim is renamed and [Authorize(Roles=...)] breaks.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
            // Our JWT carries the role in a plain "role" claim; map it so [Authorize(Roles=...)] works.
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();

// ----- CORS (for the React frontend, which now runs on its own server/port) -----
const string CorsPolicy = "MusicWebsiteCors";

// Origins come from .env first (FRONTEND_ORIGINS=http://a.com,http://b.com) and fall back to
// appsettings' Cors:AllowedOrigins. Put the PUBLIC frontend URL here when deploying — the
// localhost/LAN rule below only covers local development.
var envOrigins = (builder.Configuration["FRONTEND_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(o => o.TrimEnd('/'));

var allowedOrigins = envOrigins
    .Concat(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

// Allow the explicitly-configured origins, plus any localhost/127.0.0.1 or private-LAN address on
// any port. This means the app works whether you open it at localhost:5173, 127.0.0.1:5173, or your
// PC's LAN IP (e.g. from a phone) without editing config each time. Tighten this for a public deploy.
bool IsOriginAllowed(string origin)
{
    if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
    if (uri.Host is "localhost" or "127.0.0.1") return true;
    if (System.Net.IPAddress.TryParse(uri.Host, out var ip))
    {
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            if (b[0] == 10) return true;                          // 10.0.0.0/8
            if (b[0] == 192 && b[1] == 168) return true;          // 192.168.0.0/16
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
        }
    }
    return false;
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.SetIsOriginAllowed(IsOriginAllowed)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.Logger.LogInformation("API listening on http://{Host}:{Port} (config: {EnvFile})",
    backendHost, backendPort, envFile ?? "no .env found — using defaults");

// ----- Middleware pipeline -----
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// NOTE: No HTTPS redirection. The app is bound to plain HTTP on BACKEND_PORT (see UseUrls above)
// so it is reachable over the LAN (phones, other PCs). UseHttpsRedirection() would 307-redirect
// every request to an HTTPS port that is not being served, which just breaks the connection.
// Add HTTPS + redirection back only when a real HTTPS endpoint/cert is configured (production).

// The frontend is a SEPARATE app on its own port/server, so the API does not serve it.
// Set SERVE_FRONTEND=true in .env to fall back to the old single-origin mode (frontend build
// copied into wwwroot and served from this same port).
var serveFrontend = builder.Configuration["SERVE_FRONTEND"]
    ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

if (serveFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ----- Health checks (anonymous) -----
// The API root is a 404 by design, which hosting platforms read as "unhealthy" and use to fail a
// deploy. /health answers 200 as soon as the process is up — a liveness probe, deliberately NOT
// touching the database, so a brief database hiccup can't trigger a restart loop.
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
   .AllowAnonymous();

// /health/db is the readiness probe: use it to check the database by hand, not as the platform's
// health check path.
app.MapGet("/health/db", async (MusicWebsite.Infrastructure.Persistence.IDbConnectionFactory factory) =>
{
    try
    {
        using var db = factory.CreateConnection();
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT 1";
        await Task.Run(() => command.ExecuteScalar());
        return Results.Ok(new { status = "ok", database = "reachable" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "error", database = "unreachable", detail = ex.Message },
            statusCode: 503);
    }
}).AllowAnonymous();

// SPA fallback: any non-API, non-file route (e.g. /login, /playlist/123) returns index.html
// so React Router can handle it. API routes are matched by MapControllers first.
if (serveFrontend) app.MapFallbackToFile("index.html");

app.Run();
