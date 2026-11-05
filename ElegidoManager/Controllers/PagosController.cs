using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class PagosController : Controller
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        public PagosController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Crear(int contratacionId)
        {
            var vm = new PagoFormViewModel { ContratacionId = contratacionId };
            if (!await CargarAsync(vm)) return NotFound();
            if (vm.Saldo <= 0)
            {
                TempData["Error"] = "Esta contratación ya está liquidada.";
                return RedirectToAction("Detalle", "Contrataciones", new { id = contratacionId });
            }
            vm.Monto = vm.Saldo;
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Crear(PagoFormViewModel vm)
        {
            if (!await CargarAsync(vm)) return NotFound();

            var c = await _db.Contrataciones.FindAsync(vm.ContratacionId);
            if (c!.Estado == EstadoContratacion.Cancelada)
                ModelState.AddModelError(string.Empty, "No se pueden registrar pagos en una contratación cancelada.");
            if (vm.Monto.HasValue && vm.Monto.Value > vm.Saldo)
                ModelState.AddModelError(nameof(vm.Monto), $"El monto excede el saldo pendiente ({vm.Saldo.ToString("C", Mx)}).");

            if (!ModelState.IsValid) return View(vm);

            var monto = vm.Monto!.Value;
            // El tipo se determina automáticamente según el estado de la cuenta
            var tipo = monto >= vm.Saldo ? TipoPago.Liquidacion
                     : !vm.TienePagos ? TipoPago.Anticipo
                     : TipoPago.Abono;

            var fecha = vm.Fecha!.Value.Date == DateTime.Today ? DateTime.Now : vm.Fecha.Value.Date.AddHours(12);
            _db.Pagos.Add(new Pago
            {
                ContratacionId = vm.ContratacionId,
                Monto = monto,
                Tipo = tipo,
                Metodo = vm.Metodo,
                Fecha = fecha,
                Referencia = vm.Referencia?.Trim(),
                Notas = vm.Notas,
                FechaRegistro = DateTime.Now
            });
            await _db.SaveChangesAsync();

            var nuevoSaldo = vm.Saldo - monto;
            TempData["Exito"] = nuevoSaldo <= 0
                ? $"Pago de {monto.ToString("C", Mx)} registrado. ¡La contratación quedó liquidada!"
                : $"{tipo.Nombre()} de {monto.ToString("C", Mx)} registrado. Saldo pendiente: {nuevoSaldo.ToString("C", Mx)}.";
            return RedirectToAction("Detalle", "Contrataciones", new { id = vm.ContratacionId });
        }

        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            var pago = await _db.Pagos.FindAsync(id);
            if (pago == null) return NotFound();

            // Los documentos generados se conservan como histórico; solo se desvincula el pago
            var docs = await _db.Documentos.Where(d => d.PagoId == id).ToListAsync();
            foreach (var d in docs) d.PagoId = null;

            _db.Pagos.Remove(pago);
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Pago eliminado. El saldo se recalculó.";
            return RedirectToAction("Detalle", "Contrataciones", new { id = pago.ContratacionId });
        }

        private async Task<bool> CargarAsync(PagoFormViewModel vm)
        {
            var datos = await _db.Contrataciones
                .Where(c => c.Id == vm.ContratacionId)
                .Select(c => new
                {
                    c.Cliente.NombreCompleto,
                    c.TipoEvento,
                    Fecha = c.Evento != null ? (DateTime?)c.Evento.Inicio : null,
                    c.PrecioAcordado,
                    Pagado = c.Pagos.Sum(p => p.Monto),
                    TienePagos = c.Pagos.Any()
                })
                .FirstOrDefaultAsync();
            if (datos == null) return false;

            vm.Cliente = datos.NombreCompleto;
            vm.TipoEvento = datos.TipoEvento;
            vm.FechaEvento = datos.Fecha;
            vm.Precio = datos.PrecioAcordado;
            vm.Pagado = datos.Pagado;
            vm.TienePagos = datos.TienePagos;
            return true;
        }
    }
}
