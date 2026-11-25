using Be.Data;
using Be.Domain;
using Be.Hubs;
using Be.Infrastructure;
using Be.Options;
using Be.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<FireRulesOptions>().BindConfiguration(FireRulesOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<MarkerOptions>().BindConfiguration(MarkerOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RespawnSweeperOptions>().BindConfiguration(RespawnSweeperOptions.Section).ValidateDataAnnotations().ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("GameDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:GameDb is not set. Use `dotnet user-secrets set ConnectionStrings:GameDb \"...\"` or the ConnectionStrings__GameDb environment variable.");
builder.Services.AddDbContext<GameDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<GameNotifier>();
builder.Services.AddScoped<ScoreService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<LobbyService>();
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<FireService>();
builder.Services.AddScoped<RespawnService>();
builder.Services.AddScoped<GameStateService>();
builder.Services.AddHostedService<RespawnSweeper>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = TokenService.CreateKey(jwt.Value),
            NameClaimType = GameClaims.Subject,
            RoleClaimType = GameClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        o.Events = new JwtBearerEvents
        {
            // WebSockets cannot send headers from browsers; accept the query token only on the hub.
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(GameHub.Path))
                {
                    ctx.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GameHub.ConnectedPolicy, p => p.RequireRole(Roles.Admin, Roles.Player));

builder.Services.AddControllers().AddJsonOptions(o => GameJson.Configure(o.JsonSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GameExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services
    .AddSignalR(o =>
    {
        o.KeepAliveInterval = TimeSpan.FromSeconds(15);
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        o.MaximumReceiveMessageSize = 32 * 1024;
        o.AddFilter<GameHubFilter>();
    })
    .AddJsonProtocol(o => GameJson.Configure(o.PayloadSerializerOptions));

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>(GameHub.Path);

app.Run();
