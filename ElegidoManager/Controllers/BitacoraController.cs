using ElegidoManager.Data;
using ElegidoManager.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    /// <summary>Bitácora de seguridad: accesos y cambios (solo administrador).</summary>
    [Authorize(Roles = Catalogos.RolAdministrador)]
    public class BitacoraController : Controller
    {
        private const int Limite = 400;
        private readonly AppDbContext _db;
        public BitacoraController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index(string? q, string? tipo)
        {
            var lista = AppDbContext.ModoMongo && _db.Mongo != null
                ? await _db.Mongo.LeerBitacoraAsync(Limite)
                : await _db.Bitacoras.OrderByDescending(b => b.Id).Take(Limite).ToListAsync();

            if (tipo == "accesos")
                lista = lista.Where(b => b.Accion is "Inició sesión" or "Cerró sesión" or "Intento fallido" or "Cuenta bloqueada").ToList();
            else if (tipo == "alertas")
                lista = lista.Where(b => b.Accion is "Intento fallido" or "Cuenta bloqueada").ToList();
            else if (tipo == "cambios")
                lista = lista.Where(b => b.Accion is not ("Inició sesión" or "Cerró sesión" or "Intento fallido" or "Cuenta bloqueada")).ToList();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim();
                lista = lista.Where(b => b.Usuario.Contains(t, StringComparison.OrdinalIgnoreCase)
                                         || b.Accion.Contains(t, StringComparison.OrdinalIgnoreCase)
                                         || (b.Detalle ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ViewBag.Q = q;
            ViewBag.Tipo = tipo ?? "todo";
            return View(lista);
        }
    }
}
