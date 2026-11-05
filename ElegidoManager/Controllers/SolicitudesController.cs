using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    /// <summary>Solicitudes de evento que llegan por el bot de WhatsApp.</summary>
    [Authorize(Roles = Catalogos.RolesEquipo)]
    public class SolicitudesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        public SolicitudesController(AppDbContext db, IConfiguration config) { _db = db; _config = config; }

        public async Task<IActionResult> Index()
        {
            var lista = await _db.SolicitudesBot
                .OrderBy(s => s.Estado == EstadoSolicitud.Nueva ? 0 : 1).ThenByDescending(s => s.Creada)
                .Take(200).ToListAsync();
            return View(lista);
        }

        /// <summary>Convierte la solicitud en contratación (cliente + evento) y abre la edición para ajustar precio/anticipo.</summary>
        [HttpPost]
        public async Task<IActionResult> Aprobar(int id)
        {
            var s = await _db.SolicitudesBot.FindAsync(id);
            if (s == null) return NotFound();
            if (s.Estado != EstadoSolicitud.Nueva && s.ContratacionId != null)
                return RedirectToAction("Detalle", "Contrataciones", new { id = s.ContratacionId });

            var digitos = Formato.SoloDigitos(s.Telefono);
            if (digitos.Length == 13 && digitos.StartsWith("521")) digitos = digitos[3..];
            var telefono = Formato.Telefono(digitos);
            var soloDiez = digitos.Length >= 10 ? digitos[^10..] : digitos;

            var cliente = (await _db.Clientes.ToListAsync()).FirstOrDefault(c => Formato.SoloDigitos(c.Telefono).EndsWith(soloDiez));
            if (cliente == null)
            {
                cliente = new Cliente { NombreCompleto = s.Nombre, Telefono = telefono };
                _db.Clientes.Add(cliente);
            }

            var precioHora = decimal.TryParse(_config["Tarifas:PrecioPorHora"], NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 3500m;
            var pendiente = s.Hora == null;
            var inicio = pendiente ? s.Fecha.Date : s.Fecha.Date.Add(s.Hora!.Value);
            var c = new Contratacion
            {
                Cliente = cliente,
                TipoEvento = "Evento",
                Estado = EstadoContratacion.Confirmada,
                PrecioAcordado = precioHora * s.DuracionHoras,
                Observaciones = "Solicitud recibida por WhatsApp.",
                Evento = new Evento
                {
                    Inicio = inicio,
                    Fin = pendiente ? s.Fecha.Date.AddDays(1).AddMinutes(-1) : inicio.AddHours(s.DuracionHoras),
                    HorarioPendiente = pendiente,
                    Direccion = s.Direccion ?? "",
                    Ciudad = ""
                }
            };
            _db.Contrataciones.Add(c);
            await _db.SaveChangesAsync();

            s.Estado = EstadoSolicitud.Aprobada;
            s.ContratacionId = c.Id;
            await _db.SaveChangesAsync();

            TempData["Exito"] = "Solicitud aprobada: ya está en la agenda. Revisa el precio y registra el anticipo cuando llegue.";
            return RedirectToAction("Editar", "Contrataciones", new { id = c.Id });
        }

        [HttpPost]
        public async Task<IActionResult> Rechazar(int id)
        {
            var s = await _db.SolicitudesBot.FindAsync(id);
            if (s == null) return NotFound();
            s.Estado = EstadoSolicitud.Rechazada;
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Solicitud marcada como rechazada.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Simulador: prueba el bot desde la app, sin WhatsApp (no envía nada a nadie).</summary>
        [HttpGet]
        public IActionResult Probar() => View();

        [HttpPost]
        public async Task<IActionResult> Probar(string mensaje, [FromServices] BotWhatsApp bot)
        {
            var respuesta = await bot.ResponderAsync("simulador-" + (User.Identity?.Name ?? "admin"), "Prueba", mensaje ?? "");
            return Json(new { respuesta = respuesta ?? "(El bot no contesta: la conversación está en modo 'hablar con una persona'. Escribe menu.)" });
        }
    }
}
