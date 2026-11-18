using Microsoft.Azure.Cosmos;
using KitchenChaos.Server.Hubs;
using KitchenChaos.Server.Observability;
using KitchenChaos.Server.Repositories;
using KitchenChaos.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// El filtro mide cada llamada del GameHub (duración, conteo y errores) sin tocar los métodos.
// JWT: clave secreta desde config o generada aleatoriamente si no está definida
var jwtKey = builder.Configuration["Jwt:Key"] ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var jwtIssuer = "kitchen-chaos";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(options =>
    {
        // Cookie para la sesión web (login con Google)
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/auth/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId     = builder.Configuration["Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
        options.CallbackPath = "/auth/google/callback";
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey
        };
        // SignalR envía el token en la query string (?access_token=...)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.Request.Path.StartsWithSegments("/gamehub"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR(options => options.AddFilter<MetricsHubFilter>());
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<PreparationService>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<PlatingService>();
builder.Services.AddSingleton<DeliveryService>();
builder.Services.AddSingleton<TimerService>();
builder.Services.AddSingleton<ObstacleService>();

// Cosmos DB: cliente singleton reutilizable
builder.Services.AddSingleton(_ =>
{
    var cfg = builder.Configuration;
    var endpoint = cfg["CosmosDb:AccountEndpoint"]!;

    var options = new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Gateway,
        // El emulador Linux reporta su IP interna de Docker en vez de localhost; esto evita que el cliente la siga
        LimitToEndpoint = true,
        SerializerOptions = new() { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase }
    };

    // El emulador local usa certificado autofirmado. Solo se acepta cualquier certificado cuando el
    // endpoint es local; con una cuenta real de Azure se valida el certificado normalmente.
    if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsLoopback)
    {
        options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
    }

    return new CosmosClient(endpoint, cfg["CosmosDb:AccountKey"]!, options);
});

builder.Services.AddScoped<IPlayerProfileRepository, CosmosPlayerProfileRepository>();
builder.Services.AddScoped<PlayerProfileService>();

var app = builder.Build();

// Crear base de datos y contenedor si no existen (útil en local con el emulador)
// El juego no depende de Cosmos (los perfiles son opcionales), así que si no está disponible
// se avisa en el log y el servidor arranca igual en vez de caerse.
try
{
    using var scope = app.Services.CreateScope();
    var cosmos = scope.ServiceProvider.GetRequiredService<CosmosClient>();
    var cfg = app.Configuration;
    var db = await cosmos.CreateDatabaseIfNotExistsAsync(cfg["CosmosDb:DatabaseName"]!);
    await db.Database.CreateContainerIfNotExistsAsync(cfg["CosmosDb:ContainerName"]!, "/id");
}
catch (Exception ex)
{
    app.Logger.LogWarning("Cosmos DB no disponible, los perfiles de jugador no funcionarán: {Message}", ex.Message);
}

// Observabilidad: la cantidad de salas se lee justo antes de cada lectura de /metrics.
Metrics.DefaultRegistry.AddBeforeCollectCallback(() =>
    GameMetrics.ActiveRooms.Set(app.Services.GetRequiredService<RoomService>().RoomCount));

app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();

// Comprime HTML, CSS y JS (index.html pesa ~140 KB sin comprimir). Los WebSocket no se comprimen.
app.UseResponseCompression();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Cada petición a Azure tarda más que en local, así que se evita repetirlas:
    // las imágenes casi no cambian (1 día), el CSS se guarda 5 minutos y el HTML siempre se revalida
    // para que un despliegue nuevo se vea de inmediato.
    OnPrepareResponse = ctx =>
    {
        var extension = Path.GetExtension(ctx.File.Name).ToLowerInvariant();

        ctx.Context.Response.Headers.CacheControl = extension switch
        {
            ".webp" or ".png" or ".jpg" or ".jpeg" or ".svg" or ".ico" or ".m4a" or ".mp3" => "public,max-age=86400",
            ".css" or ".js" => "public,max-age=300",
            _ => "no-cache"
        };
    }
});

app.MapHub<GameHub>("/gamehub").RequireAuthorization();

// Redirige a Google para autenticarse
app.MapGet("/auth/login", async ctx =>
{
    var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault() ?? "/";
    await ctx.ChallengeAsync(GoogleDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = returnUrl });
});

// Google llama a este endpoint tras autenticar al usuario
app.MapGet("/auth/google/callback", async ctx =>
{
    var result = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (result.Succeeded)
    {
        ctx.Response.Redirect("/");
        return;
    }
    ctx.Response.Redirect("/auth/login");
});

// Cierra la sesión
app.MapGet("/auth/logout", async ctx =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/");
});

// API REST: genera un JWT para SignalR — solo si el usuario ya pasó por Google
app.MapPost("/api/auth/token", async ctx =>
{
    var result = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!result.Succeeded)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    var claims = new[] { new Claim(ClaimTypes.Role, "player") };
    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: creds
    );
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
});

// /metrics lo lee Prometheus; /ready lo consulta la sonda de disponibilidad.
// Si se define Metrics:Token (variable de entorno Metrics__Token), /metrics exige
// "Authorization: Bearer <token>"; así no queda abierto a internet en el despliegue.
// Sin token (por ejemplo en local) sigue abierto como antes.
var metricsToken = app.Configuration["Metrics:Token"];
if (!string.IsNullOrEmpty(metricsToken))
{
    app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/metrics"), branch =>
        branch.Use(async (ctx, next) =>
        {
            var expected = Encoding.UTF8.GetBytes($"Bearer {metricsToken}");
            var received = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());

            if (!CryptographicOperations.FixedTimeEquals(expected, received))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        }));
}

app.MapMetrics();
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));

app.Run();
