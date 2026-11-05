using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Conexión a la base en la nube para la app de escritorio (Visual Studio).
// Este archivo NO se sube a GitHub ni a Vercel; ahí se usa la variable ConnectionStrings__ElegidoManager.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// En la nube (Vercel / Docker) el puerto llega en la variable PORT
var puerto = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(puerto))
    builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");

// Licencia comunitaria de QuestPDF (gratuita para proyectos pequeños / personales)
QuestPDF.Settings.License = LicenseType.Community;

builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

// Páginas comprimidas (cargan más rápido con datos móviles)
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.AddHttpContextAccessor();

// Base de datos: si hay cadena de MongoDB se usa MongoDB Atlas; si no, SQL Server (LocalDB / Azure SQL)
var mongoUri = builder.Configuration.GetConnectionString("MongoDB");
if (string.IsNullOrWhiteSpace(mongoUri)) mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI");
if (!string.IsNullOrWhiteSpace(mongoUri))
{
    AppDbContext.ModoMongo = true;
    var baseMongo = builder.Configuration["MongoDB:BaseDatos"];
    builder.Services.AddSingleton(new MongoAlmacen(mongoUri, string.IsNullOrWhiteSpace(baseMongo) ? "ElegidoManager" : baseMongo));
    builder.Services.AddDbContext<AppDbContext>();
    // Cada petición recibe un contexto ya cargado con la información de MongoDB
    builder.Services.AddScoped(sp =>
    {
        var ctx = ActivatorUtilities.CreateInstance<AppDbContext>(sp, sp.GetRequiredService<DbContextOptions<AppDbContext>>());
        ctx.CargarDesdeMongo();
        return ctx;
    });
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("ElegidoManager")));
}

// Seguridad: máximo 10 intentos de inicio de sesión por minuto desde la misma IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "ElegidoManager.Af";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Las llaves de la sesión se guardan en la base de datos (el disco de la nube se borra)
builder.Services.AddDataProtection()
    .SetApplicationName("ElegidoManager")
    .PersistKeysToDbContext<AppDbContext>();

// Detrás del proxy de la nube: respeta https y la IP real del visitante
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Cuenta/Login";
        o.LogoutPath = "/Cuenta/Salir";
        o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.Cookie.Name = "ElegidoManager.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // Cierra la sesión si el usuario fue desactivado o cambió su contraseña / rol
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var sello = ctx.Principal?.FindFirstValue("sello") ?? "";
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var usuario = int.TryParse(id, out var uid) ? await db.Usuarios.FindAsync(uid) : null;
            if (usuario == null || !usuario.Activo || (usuario.SelloSeguridad ?? "") != sello)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews(o =>
{
    // Todo el sistema requiere sesión, salvo las acciones marcadas con [AllowAnonymous]
    o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.Configure<GrupoSettings>(builder.Configuration.GetSection("Grupo"));
builder.Services.AddScoped<AgendaService>();
builder.Services.AddScoped<ConsultasService>();
builder.Services.AddScoped<DocumentosService>();
builder.Services.AddHttpClient();
builder.Services.Configure<WhatsAppBotSettings>(builder.Configuration.GetSection("WhatsAppBot"));
builder.Services.AddScoped<BotWhatsApp>();

var cultura = new CultureInfo("es-MX");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var app = builder.Build();

// Ladas de Estados Unidos para WhatsApp (+1)
var ladasUsa = app.Configuration.GetSection("LadasEstadosUnidos").GetChildren()
    .Select(x => x.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet();
if (ladasUsa.Count > 0) WhatsApp.LadasEstadosUnidos = ladasUsa;

// Crea la base de datos y sus tablas en SQL Server si todavía no existen.
if (AppDbContext.ModoMongo)
{
    // En la nube el servidor debe abrir su puerto de inmediato: MongoDB se prepara en segundo plano
    app.Lifetime.ApplicationStarted.Register(() => Task.Run(() =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            ActualizarEsquema.AplicarMongo(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            app.Logger.LogInformation("MongoDB listo.");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "No se pudo conectar a MongoDB. Revisa la cadena ConnectionStrings__MongoDB y que Atlas → Network Access permita 0.0.0.0/0.");
        }
    }));
}
else
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Reintenta unas veces: una base en la nube "dormida" tarda en despertar
    for (var intento = 1; ; intento++)
    {
        try
        {
            db.Database.EnsureCreated();
            ActualizarEsquema.Aplicar(db);
            break;
        }
        catch (Exception ex) when (intento < 4)
        {
            app.Logger.LogWarning(ex, "SQL Server no respondió (intento {Intento}); reintentando…", intento);
            Thread.Sleep(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex, "No se pudo conectar a SQL Server. Revisa la cadena de conexión 'ElegidoManager' (appsettings.json o variable ConnectionStrings__ElegidoManager).");
            throw;
        }
    }
}

app.UseForwardedHeaders();

// Tiempo de respuesta del servidor (visible en las herramientas del navegador → Server-Timing)
app.Use(async (ctx, next) =>
{
    var reloj = System.Diagnostics.Stopwatch.StartNew();
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers["Server-Timing"] = $"app;dur={reloj.Elapsed.TotalMilliseconds:0}";
        return Task.CompletedTask;
    });
    await next();
});

// Encabezados de seguridad (anti-clickjacking, anti-XSS, sin rastreo)
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src 'self' data: blob: https:; " +
        "frame-src 'self' https://maps.google.com https://www.google.com; " +
        "connect-src 'self' https://cdn.jsdelivr.net https://fonts.googleapis.com https://fonts.gstatic.com; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
var tiposArchivo = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
tiposArchivo.Mappings[".webmanifest"] = "application/manifest+json";
app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = tiposArchivo,
    // Estilos, scripts e imágenes se guardan en el celular: no se vuelven a descargar en cada página
    OnPrepareResponse = ctx =>
    {
        var conVersion = ctx.Context.Request.QueryString.HasValue;
        ctx.Context.Response.Headers.CacheControl = ctx.File.Name == "sw.js"
            ? "no-cache"
            : conVersion ? "public, max-age=31536000, immutable" : "public, max-age=86400";
    }
});

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(cultura),
    SupportedCultures = new[] { cultura },
    SupportedUICultures = new[] { cultura }
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
