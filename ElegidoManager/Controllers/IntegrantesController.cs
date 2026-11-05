using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    public class IntegranteResumen
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public decimal MeDebe { get; set; }
        public decimal LeDebo { get; set; }
    }

    /// <summary>Control del dinero que los integrantes me deben y del que yo les debo.</summary>
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class IntegrantesController : Controller
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        public IntegrantesController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var lista = await _db.Integrantes
                .OrderBy(i => i.Id)
                .Select(i => new IntegranteResumen
                {
                    Id = i.Id,
                    Nombre = i.NombreCompleto,
                    Telefono = i.Telefono,
                    MeDebe = i.Movimientos.Where(m => !m.Liquidado && m.Direccion == DireccionDeuda.MeDebe).Sum(m => m.Monto),
                    LeDebo = i.Movimientos.Where(m => !m.Liquidado && m.Direccion == DireccionDeuda.LeDebo).Sum(m => m.Monto)
                })
                .ToListAsync();
            return View(lista);
        }

        public async Task<IActionResult> Detalle(int id)
        {
            var integrante = await _db.Integrantes
                .Include(i => i.Movimientos)
                .FirstOrDefaultAsync(i => i.Id == id);
            if (integrante == null) return NotFound();
            return View(integrante);
        }

        [HttpPost]
        public async Task<IActionResult> Agregar(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                TempData["Error"] = "Escribe el nombre del integrante.";
                return RedirectToAction(nameof(Index));
            }
            _db.Integrantes.Add(new Integrante { NombreCompleto = nombre.Trim(), Instrumento = "", Activo = true });
            await _db.SaveChangesAsync();
            TempData["Exito"] = $"{nombre.Trim()} agregado.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Editar(int id, string nombre, string? telefono)
        {
            var i = await _db.Integrantes.FindAsync(id);
            if (i == null) return NotFound();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                TempData["Error"] = "El nombre no puede quedar vacío.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            i.NombreCompleto = nombre.Trim();
            i.Telefono = string.IsNullOrWhiteSpace(telefono) ? null : Formato.Telefono(telefono);
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Datos actualizados.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            var i = await _db.Integrantes.FindAsync(id);
            if (i == null) return NotFound();
            _db.Integrantes.Remove(i); // sus movimientos se borran en cascada
            await _db.SaveChangesAsync();
            TempData["Exito"] = $"{i.NombreCompleto} eliminado.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> AgregarMovimiento(int integranteId, DireccionDeuda direccion, decimal? monto, string? concepto)
        {
            if (!await _db.Integrantes.AnyAsync(i => i.Id == integranteId)) return NotFound();
            if (!monto.HasValue || monto.Value <= 0)
            {
                TempData["Error"] = "Escribe una cantidad mayor a cero.";
                return RedirectToAction(nameof(Detalle), new { id = integranteId });
            }

            _db.MovimientosIntegrantes.Add(new MovimientoIntegrante
            {
                IntegranteId = integranteId,
                Direccion = direccion,
                Monto = monto.Value,
                Concepto = string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim(),
                Fecha = DateTime.Now,
                FechaRegistro = DateTime.Now
            });
            await _db.SaveChangesAsync();
            TempData["Exito"] = direccion == DireccionDeuda.MeDebe
                ? $"Anotado: te debe {monto.Value.ToString("C0", Mx)}."
                : $"Anotado: le debes {monto.Value.ToString("C0", Mx)}.";
            return RedirectToAction(nameof(Detalle), new { id = integranteId });
        }

        /// <summary>Marca una deuda como pagada (o la regresa a pendiente).</summary>
        [HttpPost]
        public async Task<IActionResult> Liquidar(int id)
        {
            var m = await _db.MovimientosIntegrantes.FindAsync(id);
            if (m == null) return NotFound();
            m.Liquidado = !m.Liquidado;
            m.FechaLiquidado = m.Liquidado ? DateTime.Now : null;
            await _db.SaveChangesAsync();
            TempData["Exito"] = m.Liquidado ? "Marcado como pagado." : "Regresado a pendiente.";
            return RedirectToAction(nameof(Detalle), new { id = m.IntegranteId });
        }

        [HttpPost]
        public async Task<IActionResult> EliminarMovimiento(int id)
        {
            var m = await _db.MovimientosIntegrantes.FindAsync(id);
            if (m == null) return NotFound();
            _db.MovimientosIntegrantes.Remove(m);
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Registro eliminado.";
            return RedirectToAction(nameof(Detalle), new { id = m.IntegranteId });
        }
    }
}
