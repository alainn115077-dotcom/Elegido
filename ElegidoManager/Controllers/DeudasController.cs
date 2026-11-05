using System.Globalization;
using System.Security.Claims;
using System.Text;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElegidoManager.Controllers
{
    public class MisDeudasViewModel
    {
        public Integrante? Integrante { get; set; }
        public string NombreAdmin { get; set; } = string.Empty;
        public List<MovimientoIntegrante> YoDebo { get; set; } = new();
        public List<MovimientoIntegrante> MeDeben { get; set; } = new();
        public decimal TotalYoDebo => YoDebo.Where(m => !m.Liquidado).Sum(m => m.Monto);
        public decimal TotalMeDeben => MeDeben.Where(m => !m.Liquidado).Sum(m => m.Monto);
    }

    /// <summary>Vista de solo lectura para cada integrante: lo que le debe al administrador y lo que el administrador le debe.</summary>
    [Authorize(Roles = Catalogos.RolesConDeudas)]
    public class DeudasController : Controller
    {
        private readonly AppDbContext _db;
        private readonly GrupoSettings _grupo;
        public DeudasController(AppDbContext db, IOptions<GrupoSettings> grupo)
        {
            _db = db;
            _grupo = grupo.Value;
        }

        public async Task<IActionResult> Index()
        {
            var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var usuario = await _db.Usuarios.FirstAsync(u => u.Id == id);

            // Si la cuenta no está ligada, se intenta ligar por nombre (ej. "Omar integrante" → Omar)
            if (usuario.IntegranteId == null)
            {
                var integrantes = await _db.Integrantes.ToListAsync();
                var palabras = (Normalizar(usuario.NombreCompleto) + " " + Normalizar(usuario.NombreUsuario))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var coincidencias = integrantes
                    .Where(i => Normalizar(i.NombreCompleto).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is string p && palabras.Contains(p))
                    .ToList();
                if (coincidencias.Count == 1)
                {
                    usuario.IntegranteId = coincidencias[0].Id;
                    await _db.SaveChangesAsync();
                }
            }

            var vm = new MisDeudasViewModel
            {
                NombreAdmin = (_grupo.Representante ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "el administrador"
            };
            if (string.IsNullOrEmpty(vm.NombreAdmin)) vm.NombreAdmin = "el administrador";

            if (usuario.IntegranteId != null)
            {
                vm.Integrante = await _db.Integrantes.FirstOrDefaultAsync(i => i.Id == usuario.IntegranteId);
                var movs = await _db.MovimientosIntegrantes
                    .Where(m => m.IntegranteId == usuario.IntegranteId)
                    .OrderBy(m => m.Liquidado).ThenByDescending(m => m.Fecha)
                    .ToListAsync();
                // MeDebe (desde el administrador) = el integrante debe; LeDebo = el administrador le debe al integrante
                vm.YoDebo = movs.Where(m => m.Direccion == DireccionDeuda.MeDebe).ToList();
                vm.MeDeben = movs.Where(m => m.Direccion == DireccionDeuda.LeDebo).ToList();
            }
            return View(vm);
        }

        private static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var c in texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
