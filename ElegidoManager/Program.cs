using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Licencia comunitaria de QuestPDF (gratuita para proyectos pequeños / personales)
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ElegidoManager")));

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

var cultura = new CultureInfo("es-MX");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var app = builder.Build();

// Ladas de Estados Unidos para WhatsApp (+1)
var ladasUsa = app.Configuration.GetSection("LadasEstadosUnidos").GetChildren()
    .Select(x => x.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet();
if (ladasUsa.Count > 0) WhatsApp.LadasEstadosUnidos = ladasUsa;

// Crea la base de datos y sus tablas en SQL Server si todavía no existen.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
        ActualizarEsquema.Aplicar(db);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "No se pudo conectar a SQL Server. Revisa la cadena de conexión 'ElegidoManager' en appsettings.json.");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
var tiposArchivo = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
tiposArchivo.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = tiposArchivo });

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(cultura),
    SupportedCultures = new[] { cultura },
    SupportedUICultures = new[] { cultura }
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
