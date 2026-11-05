using System.Text.Json;
using ElegidoManager.Data;
using ElegidoManager.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElegidoManager.Controllers
{
    /// <summary>Respaldo completo de la información en un archivo JSON (solo administrador).</summary>
    [Authorize(Roles = Catalogos.RolAdministrador)]
    public class RespaldoController : Controller
    {
        // Nunca salen en el respaldo: contraseñas, llaves de sesión ni PDF (pesan mucho)
        private static readonly HashSet<string> Omitir = new()
        {
            nameof(Usuario.PasswordHash), nameof(Usuario.SelloSeguridad), nameof(Documento.Contenido)
        };

        private readonly AppDbContext _db;
        public RespaldoController(AppDbContext db) => _db = db;

        [HttpGet]
        public IActionResult Descargar()
        {
            var datos = new Dictionary<string, object>
            {
                ["generado"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["sistema"] = "ELEGIDO MANAGER"
            };

            foreach (var tipo in _db.Model.GetEntityTypes())
            {
                if (tipo.ClrType == typeof(Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey)) continue;
                if (tipo.ClrType == typeof(Bitacora)) continue;

                var props = tipo.GetProperties().Where(p => p.PropertyInfo != null && !Omitir.Contains(p.Name)).ToList();
                var set = (IQueryable<object>)typeof(Microsoft.EntityFrameworkCore.DbContext)
                    .GetMethod(nameof(Microsoft.EntityFrameworkCore.DbContext.Set), Type.EmptyTypes)!
                    .MakeGenericMethod(tipo.ClrType).Invoke(_db, null)!;

                datos[tipo.ClrType.Name] = set.AsEnumerable()
                    .Select(e => props.ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(e)))
                    .ToList();
            }

            _db.Bitacoras.Add(_db.NuevaBitacora("Descargó respaldo", null));
            _db.SaveChanges();

            var json = JsonSerializer.SerializeToUtf8Bytes(datos, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            return File(json, "application/json", $"respaldo-elegido-{DateTime.Now:yyyy-MM-dd-HHmm}.json");
        }
    }
}
