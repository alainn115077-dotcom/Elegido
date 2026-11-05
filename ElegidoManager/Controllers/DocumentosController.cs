using System.Security.Claims;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class DocumentosController : Controller
    {
        private readonly AppDbContext _db;
        private readonly DocumentosService _docs;
        private readonly ILogger<DocumentosController> _log;

        public DocumentosController(AppDbContext db, DocumentosService docs, ILogger<DocumentosController> log)
        {
            _db = db;
            _docs = docs;
            _log = log;
        }

        public async Task<IActionResult> Index(TipoDocumento? tipo, string? q)
        {
            // Solo se manejan contratos de servicios
            var consulta = _db.Documentos.Where(d => d.Tipo == TipoDocumento.Contrato);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim();
                consulta = consulta.Where(d => d.Folio.Contains(t) || d.Contratacion.Cliente.NombreCompleto.Contains(t));
            }

            // Proyección sin la columna Contenido para no cargar los PDF en memoria
            var lista = await consulta.OrderByDescending(d => d.FechaGeneracion).Take(500)
                .Select(d => new DocumentoFila
                {
                    Id = d.Id,
                    Folio = d.Folio,
                    Tipo = d.Tipo,
                    FechaGeneracion = d.FechaGeneracion,
                    GeneradoPor = d.GeneradoPor,
                    ContratacionId = d.ContratacionId,
                    Cliente = d.Contratacion.Cliente.NombreCompleto,
                    TipoEvento = d.Contratacion.TipoEvento,
                    FechaEvento = d.Contratacion.Evento != null ? (DateTime?)d.Contratacion.Evento.Inicio : null,
                    MontoPago = d.Pago != null ? (decimal?)d.Pago.Monto : null
                }).ToListAsync();

            ViewData["Tipo"] = tipo;
            ViewData["Q"] = q;
            return View(lista);
        }

        [HttpPost]
        public async Task<IActionResult> Generar(int contratacionId, TipoDocumento tipo, int? pagoId)
        {
            if (tipo != TipoDocumento.Contrato)
            {
                TempData["Error"] = "Solo se generan contratos de servicios.";
                return RedirectToAction("Detalle", "Contrataciones", new { id = contratacionId });
            }
            try
            {
                var usuario = User.FindFirstValue("NombreCompleto") ?? User.Identity?.Name ?? "Sistema";
                var doc = await _docs.GenerarAsync(contratacionId, tipo, pagoId, usuario);
                TempData["Exito"] = $"{doc.Tipo.Nombre()} generado con folio {doc.Folio}.";
                TempData["AbrirDocumento"] = doc.Id;
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error al generar documento {Tipo} para contratación {Id}", tipo, contratacionId);
                TempData["Error"] = "No se pudo generar el documento PDF. Revisa el registro de errores.";
            }
            return RedirectToAction("Detalle", "Contrataciones", new { id = contratacionId });
        }

        public async Task<IActionResult> Ver(int id)
        {
            var doc = await _db.Documentos.Where(d => d.Id == id)
                .Select(d => new { d.Contenido, d.NombreArchivo }).FirstOrDefaultAsync();
            if (doc == null) return NotFound();
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{doc.NombreArchivo}\"";
            return File(doc.Contenido, "application/pdf");
        }

        public async Task<IActionResult> Descargar(int id)
        {
            var doc = await _db.Documentos.Where(d => d.Id == id)
                .Select(d => new { d.Contenido, d.NombreArchivo }).FirstOrDefaultAsync();
            if (doc == null) return NotFound();
            return File(doc.Contenido, "application/pdf", doc.NombreArchivo);
        }
    }
}
