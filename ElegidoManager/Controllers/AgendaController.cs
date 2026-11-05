using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    public class AgendaController : Controller
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        private readonly AgendaService _agenda;

        public AgendaController(AppDbContext db, AgendaService agenda)
        {
            _db = db;
            _agenda = agenda;
        }

        public IActionResult Index() => View();

        /// <summary>Los integrantes del grupo solo ven fechas, horarios y lugares (sin pagos ni datos del cliente).</summary>
        private bool SoloVer => User.IsInRole(Catalogos.RolIntegrante);

        /// <summary>Fuente de datos JSON del calendario (FullCalendar envía start y end).</summary>
        [HttpGet]
        public async Task<IActionResult> Eventos(DateTime start, DateTime end, bool canceladas = true)
        {
            // Se piden un día antes y después para detectar empalmes con eventos vecinos
            var consulta = _db.Eventos.Where(e => e.Inicio < end.AddDays(1) && e.Fin > start.AddDays(-1));
            if (!canceladas) consulta = consulta.Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada);

            var datos = await consulta.Select(e => new
            {
                e.ContratacionId,
                e.Inicio,
                e.Fin,
                e.Lugar,
                e.Ciudad,
                e.Direccion,
                e.HorarioPendiente,
                Cliente = e.Contratacion.Cliente.NombreCompleto,
                e.Contratacion.TipoEvento,
                e.Contratacion.Estado,
                e.Contratacion.PrecioAcordado,
                Pagado = e.Contratacion.Pagos.Sum(p => p.Monto)
            }).ToListAsync();

            // Eventos que se registraron aunque chocan con otro (respetando el margen de traslado)
            var margen = TimeSpan.FromMinutes(_agenda.MargenMinutos);
            var activos = datos.Where(e => e.Estado != EstadoContratacion.Cancelada && !e.HorarioPendiente).ToList();
            var empalmados = activos
                .Where(a => activos.Any(b => b.ContratacionId != a.ContratacionId && a.Inicio < b.Fin + margen && a.Fin > b.Inicio - margen))
                .Select(a => a.ContratacionId)
                .ToHashSet();

            var resultado = datos.Where(e => e.Inicio < end && e.Fin > start).Select(e => new
            {
                id = e.ContratacionId,
                title = e.Cliente,
                start = e.HorarioPendiente ? e.Inicio.ToString("yyyy-MM-dd") : e.Inicio.ToString("yyyy-MM-ddTHH:mm:ss"),
                end = e.HorarioPendiente ? e.Inicio.AddDays(1).ToString("yyyy-MM-dd") : e.Fin.ToString("yyyy-MM-ddTHH:mm:ss"),
                allDay = e.HorarioPendiente,
                url = SoloVer ? null : Url.Action("Detalle", "Contrataciones", new { id = e.ContratacionId }),
                classNames = empalmados.Contains(e.ContratacionId)
                    ? new[] { "ev-" + e.Estado.ToString().ToLower(), "ev-empalme" }
                    : new[] { "ev-" + e.Estado.ToString().ToLower() },
                extendedProps = new
                {
                    cliente = e.Cliente,
                    tipo = e.TipoEvento,
                    estado = e.Estado.Nombre(),
                    estadoClase = e.Estado.Clase(),
                    empalme = empalmados.Contains(e.ContratacionId),
                    lugar = string.Join(", ", new[] { e.Lugar, e.Direccion, e.Ciudad }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } u ? u : "Lugar pendiente",
                    horario = Textos.Horario(e.Inicio, e.Fin, e.HorarioPendiente),
                    saldo = SoloVer ? null : (e.PrecioAcordado - e.Pagado).ToString("C0", Mx)
                }
            });
            return Json(resultado);
        }

        /// <summary>Archivo .ics de una presentación para agregarla al calendario del celular.</summary>
        [HttpGet]
        public async Task<IActionResult> Ics(int id, [FromServices] Microsoft.Extensions.Options.IOptions<GrupoSettings> grupo)
        {
            var e = await _db.Eventos.Include(x => x.Contratacion).ThenInclude(c => c.Cliente)
                .FirstOrDefaultAsync(x => x.ContratacionId == id);
            if (e == null) return NotFound();
            var texto = Calendario.Generar(new[] { e }, grupo.Value.Nombre, $"{Request.Scheme}://{Request.Host}/Agenda");
            return File(System.Text.Encoding.UTF8.GetBytes(texto), "text/calendar", $"presentacion-{e.Inicio:yyyy-MM-dd}.ics");
        }

        /// <summary>Todas las presentaciones próximas en un solo archivo .ics.</summary>
        [HttpGet]
        public async Task<IActionResult> IcsProximos([FromServices] Microsoft.Extensions.Options.IOptions<GrupoSettings> grupo)
        {
            var hoy = DateTime.Today;
            var eventos = await _db.Eventos.Include(x => x.Contratacion).ThenInclude(c => c.Cliente)
                .Where(x => x.Fin >= hoy && x.Contratacion.Estado != EstadoContratacion.Cancelada)
                .OrderBy(x => x.Inicio).ToListAsync();
            var texto = Calendario.Generar(eventos, grupo.Value.Nombre, $"{Request.Scheme}://{Request.Host}/Agenda");
            return File(System.Text.Encoding.UTF8.GetBytes(texto), "text/calendar", "presentaciones-elegido.ics");
        }

        /// <summary>Detalle de un día para la ventana de la agenda: eventos con datos de contacto y enlaces.</summary>
        [HttpGet]
        public async Task<IActionResult> Dia(DateTime fecha)
        {
            var d = fecha.Date;
            var sig = d.AddDays(1);
            var eventos = await _db.Eventos
                .Include(e => e.Contratacion).ThenInclude(c => c.Cliente)
                .Where(e => e.Inicio >= d && e.Inicio < sig && e.Contratacion.Estado != EstadoContratacion.Cancelada)
                .OrderBy(e => e.HorarioPendiente).ThenBy(e => e.Inicio)
                .ToListAsync();

            string Cap(string t) => string.IsNullOrEmpty(t) ? t : char.ToUpper(t[0]) + t.Substring(1);

            return Json(new
            {
                titulo = Cap(d.ToString("dddd d 'de' MMMM", Mx)),
                pasado = d < DateTime.Today,
                soloVer = SoloVer,
                urlNueva = SoloVer ? null : Url.Action("Crear", "Contrataciones", new { fecha = d.ToString("yyyy-MM-dd") }),
                eventos = eventos.Select(e => new
                {
                    cliente = e.Contratacion.Cliente.NombreCompleto,
                    telefono = SoloVer ? null : Formato.Telefono(e.Contratacion.Cliente.Telefono),
                    whatsapp = SoloVer ? null : WhatsApp.Enlace(e.Contratacion.Cliente.Telefono),
                    inicio = e.HorarioPendiente ? null : e.Inicio.ToString("h:mm tt", Mx),
                    fin = e.HorarioPendiente ? null : e.Fin.ToString("h:mm tt", Mx),
                    horario = e.HorarioTexto,
                    ubicacion = e.Ubicacion,
                    mapa = e.LugarPendiente ? null : Mapas.Url(e.Ubicacion),
                    urlDetalle = SoloVer ? null : Url.Action("Detalle", "Contrataciones", new { id = e.ContratacionId }),
                    urlEditar = SoloVer ? null : Url.Action("Editar", "Contrataciones", new { id = e.ContratacionId })
                })
            });
        }

        /// <summary>Verifica en vivo si un horario está libre (usado por el formulario de contratación).</summary>
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
        public async Task<IActionResult> Disponibilidad(DateTime fecha, TimeSpan? horaInicio, TimeSpan? horaFin, int? excluir)
        {
            var delDia = await _agenda.EventosDelDiaAsync(fecha);
            var otros = delDia.Where(e => excluir == null || e.ContratacionId != excluir).ToList();

            var conflictos = new List<Evento>();
            if (horaInicio.HasValue && horaFin.HasValue && horaInicio != horaFin)
            {
                var (ini, fin) = AgendaService.CalcularRango(fecha, horaInicio.Value, horaFin.Value);
                conflictos = await _agenda.BuscarConflictosAsync(ini, fin, excluir);
            }

            return Json(new
            {
                fecha = fecha.ToString("dddd d 'de' MMMM", Mx),
                diaLibre = otros.Count == 0,
                disponible = conflictos.Count == 0,
                margen = _agenda.MargenMinutos,
                eventos = otros.Select(e => new
                {
                    cliente = e.Contratacion.Cliente.NombreCompleto,
                    horario = e.HorarioTexto,
                    lugar = e.Ubicacion,
                    estado = e.Contratacion.Estado.Nombre()
                }),
                conflictos = conflictos.Select(e => new
                {
                    cliente = e.Contratacion.Cliente.NombreCompleto,
                    horario = $"{e.Inicio.ToString("d MMM h:mm tt", Mx)} – {e.Fin.ToString("h:mm tt", Mx)}"
                })
            });
        }

        /// <summary>Historial de eventos ya realizados.</summary>
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
        public async Task<IActionResult> Anteriores(int? anio)
        {
            var ahora = DateTime.Now;
            var consulta = _db.Eventos.Where(e => e.Fin < ahora);
            if (anio.HasValue) consulta = consulta.Where(e => e.Inicio.Year == anio.Value);

            var lista = await consulta.OrderByDescending(e => e.Inicio).Take(500)
                .Select(e => new EventoResumen
                {
                    ContratacionId = e.ContratacionId,
                    Cliente = e.Contratacion.Cliente.NombreCompleto,
                    Telefono = e.Contratacion.Cliente.Telefono,
                    TipoEvento = e.Contratacion.TipoEvento,
                    Inicio = e.Inicio,
                    Fin = e.Fin,
                    Lugar = e.Lugar,
                    Direccion = e.Direccion,
                    Ciudad = e.Ciudad,
                    Estado = e.Contratacion.Estado,
                    HorarioPendiente = e.HorarioPendiente,
                    Precio = e.Contratacion.PrecioAcordado,
                    Pagado = e.Contratacion.Pagos.Sum(p => p.Monto)
                }).ToListAsync();

            ViewData["Anios"] = await _db.Eventos.Where(e => e.Fin < ahora).Select(e => e.Inicio.Year).Distinct().OrderByDescending(y => y).ToListAsync();
            ViewData["Anio"] = anio;
            return View(lista);
        }
    }
}
