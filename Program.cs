using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using SkyOpsQueueIntelligence.Hubs;
using SkyOpsQueueIntelligence.Infrastructure.Configuration;
using SkyOpsQueueIntelligence.Infrastructure.Filters;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Middleware;
using SkyOpsQueueIntelligence.Tools;

var builder = WebApplication.CreateBuilder(args);

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT:SecretKey not configured in appsettings.json");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = "SkyOpsQueueIntelligence",
        ValidateAudience = true,
        ValidAudience = "SkyOpsQueueIntelligence",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

var corsOrigin = builder.Configuration["Cors:Origin"] ?? throw new InvalidOperationException("Cors:Origin not configured in appsettings.json");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy
            .WithOrigins(corsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ExceptionLoggingFilter>();
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<Queue7AnalysisTools>();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseCors("AngularPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ApiRequestLoggingMiddleware>();

var connectionCredentialStore = app.Services.GetRequiredService<IConnectionCredentialStore>();
await connectionCredentialStore.LoadAsync();
// Load PCC credentials after the shared connection credentials are available.
var credentialStore = app.Services.GetRequiredService<ICredentialStore>();
await credentialStore.LoadAsync();

app.MapControllers();
app.MapHub<QueueNotificationsHub>("/queue-notifications");
app.MapMcp("/mcp");
app.MapGet("/", () => "SkyOps Queue Intelligence MCP Running - analysis only (queues 7, 379, 62)");

var appUrl = builder.Configuration["EmailNotification:BaseUrl"];
if (!string.IsNullOrEmpty(appUrl) && builder.Environment.IsDevelopment())
    app.Run(appUrl);
else
    app.Run();
