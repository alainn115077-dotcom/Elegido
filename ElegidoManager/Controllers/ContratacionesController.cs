using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElegidoManager.Controllers
{
    public class ContratacionesIndexViewModel
    {
        public List<EventoResumen> Items { get; set; } = new();
        public string? Estado { get; set; }
        public string? Q { get; set; }
        public string Vista { get; set; } = "proximas";
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
    }

    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class ContratacionesController : Controller
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        private readonly AgendaService _agenda;
        private readonly List<string> _avisosEmpalme = new();
        private readonly GrupoSettings _grupo;

        public ContratacionesController(AppDbContext db, AgendaService agenda, IOptions<GrupoSettings> grupo)
        {
            _db = db;
            _agenda = agenda;
            _grupo = grupo.Value;
        }

        // ------------------------------------------------------------------ LISTADO
        public async Task<IActionResult> Index(string? estado, string? q, string vista = "proximas", DateTime? desde = null, DateTime? hasta = null)
        {
            var ahora = DateTime.Now;
            var consulta = _db.Eventos.AsQueryable();

            if (Enum.TryParse<EstadoContratacion>(estado, out var est))
                consulta = consulta.Where(e => e.Contratacion.Estado == est);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLower();
                consulta = consulta.Where(e => e.Contratacion.Cliente.NombreCompleto.ToLower().Contains(t)
                                               || (e.Contratacion.Cliente.Telefono ?? "").Contains(t)
                                               || (e.Direccion ?? "").ToLower().Contains(t)
                                               || (e.Ciudad ?? "").ToLower().Contains(t));
            }

            if (desde.HasValue) consulta = consulta.Where(e => e.Inicio >= desde.Value.Date);
            if (hasta.HasValue) consulta = consulta.Where(e => e.Inicio < hasta.Value.Date.AddDays(1));

            consulta = vista switch
            {
                "anteriores" => consulta.Where(e => e.Fin < ahora).OrderByDescending(e => e.Inicio),
                "todas" => consulta.OrderByDescending(e => e.Inicio),
                _ => consulta.Where(e => e.Fin >= ahora).OrderBy(e => e.Inicio)
            };

            var vm = new ContratacionesIndexViewModel
            {
                Estado = estado, Q = q, Vista = vista, Desde = desde, Hasta = hasta,
                Items = await consulta.Take(300).Select(e => new EventoResumen
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
                }).ToListAsync()
            };
            return View(vm);
        }

        // ------------------------------------------------------------------ DETALLE
        public async Task<IActionResult> Detalle(int id)
        {
            var c = await _db.Contrataciones
                .Include(x => x.Cliente)
                .Include(x => x.Servicio)
                .Include(x => x.Evento)
                .Include(x => x.Pagos)
                .Include(x => x.Gastos)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            var vm = new ContratacionDetalleViewModel
            {
                Contratacion = c,
                Plantillas = WhatsApp.Plantillas(c, _grupo),
                Documentos = await _db.Documentos.Where(d => d.ContratacionId == id && d.Tipo == TipoDocumento.Contrato)
                    .OrderByDescending(d => d.FechaGeneracion)
                    .Select(d => new DocumentoFila
                    {
                        Id = d.Id, Folio = d.Folio, Tipo = d.Tipo, FechaGeneracion = d.FechaGeneracion,
                        GeneradoPor = d.GeneradoPor, ContratacionId = d.ContratacionId,
                        MontoPago = d.Pago != null ? (decimal?)d.Pago.Monto : null
                    }).ToListAsync()
            };

            if (c.Evento != null && c.Estado != EstadoContratacion.Cancelada)
                vm.Conflictos = await _agenda.BuscarConflictosAsync(c.Evento.Inicio, c.Evento.Fin, c.Id);

            return View(vm);
        }

        // ------------------------------------------------------------------ CREAR
        public async Task<IActionResult> Crear(DateTime? fecha, int? clienteId)
        {
            var vm = new ContratacionFormViewModel
            {
                Dia = fecha?.Day,
                Mes = fecha?.Month,
                ClienteId = clienteId,
                NuevoCliente = !clienteId.HasValue
            };
            await CargarListasAsync(vm);
            return View("Form", vm);
        }

        [HttpPost]
        public async Task<IActionResult> Crear(ContratacionFormViewModel vm)
        {
            vm.Estado = EstadoContratacion.Confirmada;
            var rango = await ValidarAsync(vm, null);
            if (vm.Anticipo.HasValue && vm.PrecioAcordado.HasValue && vm.Anticipo > vm.PrecioAcordado)
                ModelState.AddModelError(nameof(vm.Anticipo), "El anticipo no puede ser mayor al precio acordado.");

            if (!ModelState.IsValid || rango == null)
            {
                await CargarListasAsync(vm);
                return View("Form", vm);
            }

            var cliente = await ObtenerOCrearClienteAsync(vm);
            var (inicio, fin) = rango.Value;

            var contratacion = new Contratacion
            {
                Cliente = cliente,
                TipoEvento = TipoEventoPorDefecto,
                Estado = EstadoContratacion.Confirmada,
                PrecioAcordado = vm.PrecioAcordado!.Value,
                Observaciones = vm.Observaciones,
                FechaRegistro = DateTime.Now,
                Evento = new Evento
                {
                    Inicio = inicio,
                    Fin = fin,
                    Direccion = vm.LugarPendiente ? string.Empty : Formato.Direccion(vm.Direccion),
                    Ciudad = string.Empty,
                    HorarioPendiente = vm.HorarioPendiente
                }
            };

            if (vm.Anticipo.HasValue && vm.Anticipo.Value > 0)
            {
                contratacion.Pagos.Add(new Pago
                {
                    Monto = vm.Anticipo.Value,
                    Tipo = vm.Anticipo.Value >= contratacion.PrecioAcordado ? TipoPago.Liquidacion : TipoPago.Anticipo,
                    Metodo = MetodoPago.Transferencia,
                    Fecha = DateTime.Now,
                    Notas = "Registrado al crear la contratación"
                });
            }

            _db.Contrataciones.Add(contratacion);
            await _db.SaveChangesAsync();

            TempData["Exito"] = $"Contratación registrada para el {inicio.ToString("dddd d 'de' MMMM 'de' yyyy", Mx)}. Saldo pendiente: {contratacion.Saldo.ToString("C", Mx)}.";
            if (_avisosEmpalme.Count > 0) TempData["Error"] = "Se registró, pero ojo: " + string.Join(" ", _avisosEmpalme) + " En la agenda sale en amarillo.";
            return RedirectToAction(nameof(Detalle), new { id = contratacion.Id });
        }

        // ------------------------------------------------------------------ EDITAR
        public async Task<IActionResult> Editar(int id)
        {
            var c = await _db.Contrataciones.Include(x => x.Cliente).Include(x => x.Evento).Include(x => x.Pagos).FirstOrDefaultAsync(x => x.Id == id);
            if (c == null || c.Evento == null) return NotFound();

            var anticipo = c.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto);
            var vm = new ContratacionFormViewModel
            {
                Id = c.Id,
                ClienteId = c.ClienteId,
                NuevoCliente = false,
                ClienteNombre = c.Cliente.NombreCompleto,
                ClienteTelefono = Formato.Telefono(c.Cliente.Telefono),
                Anticipo = anticipo > 0 ? anticipo : null,
                OtrosPagos = c.TotalPagado - anticipo,
                Estado = c.Estado,
                Dia = c.Evento.Inicio.Day,
                Mes = c.Evento.Inicio.Month,
                DiaOriginal = c.Evento.Inicio.Day,
                MesOriginal = c.Evento.Inicio.Month,
                AnioOriginal = c.Evento.Inicio.Year,
                HoraInicio = c.Evento.HorarioPendiente ? null : c.Evento.Inicio.TimeOfDay,
                DuracionHoras = c.Evento.HorarioPendiente ? null : Math.Round((decimal)(c.Evento.Fin - c.Evento.Inicio).TotalHours, 2),
                HorarioPendiente = c.Evento.HorarioPendiente,
                Direccion = c.Evento.UbicacionReal,
                LugarPendiente = c.Evento.LugarPendiente,
                PrecioAcordado = c.PrecioAcordado,
                Observaciones = c.Observaciones,
                TotalPagado = c.TotalPagado
            };
            await CargarListasAsync(vm);
            return View("Form", vm);
        }

        [HttpPost]
        public async Task<IActionResult> Editar(int id, ContratacionFormViewModel vm)
        {
            if (id != vm.Id) return BadRequest();
            var c = await _db.Contrataciones
                .Include(x => x.Cliente).Include(x => x.Evento).Include(x => x.Pagos).Include(x => x.Documentos)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c == null || c.Evento == null) return NotFound();

            var anticipos = c.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).OrderBy(p => p.Fecha).ToList();
            var otrosPagos = c.Pagos.Where(p => p.Tipo != TipoPago.Anticipo).Sum(p => p.Monto);
            vm.TotalPagado = c.TotalPagado;
            vm.OtrosPagos = otrosPagos;
            vm.Estado = c.Estado; // el estado se cambia desde el detalle, no desde este formulario
            vm.NuevoCliente = false;
            vm.ClienteId = c.ClienteId;
            var rango = await ValidarAsync(vm, id);

            var nuevoAnticipo = vm.Anticipo ?? 0;
            if (vm.PrecioAcordado.HasValue && vm.PrecioAcordado.Value < otrosPagos + nuevoAnticipo)
                ModelState.AddModelError(nameof(vm.PrecioAcordado), $"El precio no puede ser menor a lo ya pagado ({(otrosPagos + nuevoAnticipo).ToString("C", Mx)}).");

            if (!ModelState.IsValid || rango == null)
            {
                await CargarListasAsync(vm);
                return View("Form", vm);
            }

            var (inicio, fin) = rango.Value;

            // Datos del cliente (se corrigen directamente en su registro)
            c.Cliente.NombreCompleto = vm.ClienteNombre!.Trim();
            c.Cliente.Telefono = Formato.Telefono(vm.ClienteTelefono);

            // Anticipo editable
            if (nuevoAnticipo > 0)
            {
                if (anticipos.Count == 0)
                {
                    c.Pagos.Add(new Pago { Monto = nuevoAnticipo, Tipo = TipoPago.Anticipo, Metodo = MetodoPago.Transferencia, Fecha = DateTime.Now, FechaRegistro = DateTime.Now });
                }
                else
                {
                    anticipos[0].Monto = nuevoAnticipo;
                    QuitarPagos(c, anticipos.Skip(1));
                }
            }
            else if (anticipos.Count > 0)
            {
                QuitarPagos(c, anticipos);
            }

            c.PrecioAcordado = vm.PrecioAcordado!.Value;
            c.Observaciones = vm.Observaciones;
            c.FechaActualizacion = DateTime.Now;

            var direccion = vm.LugarPendiente ? string.Empty : Formato.Direccion(vm.Direccion);
            if (direccion != c.Evento.UbicacionReal)
            {
                // La dirección capturada sustituye a lugar/ciudad anteriores
                c.Evento.Lugar = null;
                c.Evento.Ciudad = string.Empty;
                c.Evento.Direccion = direccion;
            }
            c.Evento.Inicio = inicio;
            c.Evento.Fin = fin;
            c.Evento.HorarioPendiente = vm.HorarioPendiente;

            await _db.SaveChangesAsync();
            TempData["Exito"] = "Contratación actualizada.";
            if (_avisosEmpalme.Count > 0) TempData["Error"] = "Se guardó, pero ojo: " + string.Join(" ", _avisosEmpalme) + " En la agenda sale en amarillo.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        // ------------------------------------------------------------------ ESTADO
        [HttpPost]
        public async Task<IActionResult> CambiarEstado(int id, EstadoContratacion estado, string? motivo)
        {
            var c = await _db.Contrataciones.Include(x => x.Evento).FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            if (c.Estado == EstadoContratacion.Cancelada && estado != EstadoContratacion.Cancelada && c.Evento != null)
            {
                var conflictos = await _agenda.BuscarConflictosAsync(c.Evento.Inicio, c.Evento.Fin, c.Id);
                if (conflictos.Count > 0)
                    TempData["Error"] = "Se reactivó, pero el horario se empalma con " +
                                        string.Join(", ", conflictos.Select(x => $"{x.Contratacion.Cliente.NombreCompleto} ({x.Inicio:h:mm tt})")) +
                                        ". En la agenda sale en amarillo.";
            }

            c.Estado = estado;
            c.MotivoCancelacion = estado == EstadoContratacion.Cancelada ? motivo?.Trim() : null;
            c.FechaActualizacion = DateTime.Now;
            await _db.SaveChangesAsync();

            TempData["Exito"] = estado switch
            {
                EstadoContratacion.Confirmada => "Contratación confirmada.",
                EstadoContratacion.Cancelada => "Contratación cancelada. La fecha quedó liberada en la agenda.",
                _ => "La contratación quedó como pendiente."
            };
            return RedirectToAction(nameof(Detalle), new { id });
        }

        // ------------------------------------------------------------------ ELIMINAR DEFINITIVAMENTE
        /// <summary>
        /// Borra la contratación por completo: su evento, pagos y documentos PDF.
        /// Los gastos que estaban ligados se conservan como gastos generales (es dinero que sí se gastó).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            var c = await _db.Contrataciones
                .Include(x => x.Cliente)
                .Include(x => x.Evento)
                .Include(x => x.Pagos)
                .Include(x => x.Documentos)
                .Include(x => x.Gastos)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            var cliente = c.Cliente.NombreCompleto;
            await using var tx = await _db.Database.BeginTransactionAsync();
            _db.Documentos.RemoveRange(c.Documentos);
            _db.Pagos.RemoveRange(c.Pagos);
            foreach (var g in c.Gastos) g.ContratacionId = null;
            if (c.Evento != null) _db.Eventos.Remove(c.Evento);
            _db.Contrataciones.Remove(c);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Exito"] = $"La contratación de {cliente} se borró definitivamente.";
            return RedirectToAction(nameof(Index));
        }

        // ------------------------------------------------------------------ AUXILIARES
        private const string TipoEventoPorDefecto = "Evento";

        /// <summary>
        /// Convierte día y mes en una fecha completa: la próxima vez que ocurra ese día (hoy incluido).
        /// En edición, si el día y el mes no cambiaron, conserva el año original del evento.
        /// </summary>
        public static DateTime? ResolverFecha(int? dia, int? mes, int? diaOriginal = null, int? mesOriginal = null, int? anioOriginal = null)
        {
            if (!dia.HasValue || !mes.HasValue || mes < 1 || mes > 12 || dia < 1 || dia > 31) return null;
            int d = dia.Value, m = mes.Value;

            if (anioOriginal.HasValue && diaOriginal == d && mesOriginal == m && d <= DateTime.DaysInMonth(anioOriginal.Value, m))
                return new DateTime(anioOriginal.Value, m, d);

            var hoy = DateTime.Today;
            for (var anio = hoy.Year; anio <= hoy.Year + 8; anio++)
            {
                if (d > DateTime.DaysInMonth(anio, m)) continue;
                var f = new DateTime(anio, m, d);
                if (f >= hoy) return f;
            }
            return null;
        }

        /// <summary>Valida el formulario y devuelve el rango inicio-fin calculado (o null si hay errores de fecha).</summary>
        private async Task<(DateTime Inicio, DateTime Fin)?> ValidarAsync(ContratacionFormViewModel vm, int? excluirId)
        {
            // Cliente
            if (vm.Id > 0)
            {
                if (string.IsNullOrWhiteSpace(vm.ClienteNombre))
                    ModelState.AddModelError(nameof(vm.ClienteNombre), "Escribe el nombre del cliente.");
                if (string.IsNullOrWhiteSpace(vm.ClienteTelefono))
                    ModelState.AddModelError(nameof(vm.ClienteTelefono), "Escribe el teléfono del cliente.");
                else
                {
                    var digitos = Formato.SoloDigitos(vm.ClienteTelefono);
                    var duplicado = await _db.Clientes.AnyAsync(x => x.Id != vm.ClienteId && x.Telefono.Replace(" ", "").Replace("-", "") == digitos);
                    if (duplicado) ModelState.AddModelError(nameof(vm.ClienteTelefono), "Ese teléfono ya pertenece a otro cliente.");
                }
                if (vm.Anticipo.HasValue && vm.PrecioAcordado.HasValue && vm.Anticipo > vm.PrecioAcordado)
                    ModelState.AddModelError(nameof(vm.Anticipo), "El anticipo no puede ser mayor al precio acordado.");
            }
            else if (vm.NuevoCliente)
            {
                if (string.IsNullOrWhiteSpace(vm.ClienteNombre))
                    ModelState.AddModelError(nameof(vm.ClienteNombre), "Escribe el nombre del cliente.");
                if (string.IsNullOrWhiteSpace(vm.ClienteTelefono))
                    ModelState.AddModelError(nameof(vm.ClienteTelefono), "Escribe el teléfono del cliente.");
            }
            else if (!vm.ClienteId.HasValue || !await _db.Clientes.AnyAsync(c => c.Id == vm.ClienteId))
            {
                ModelState.AddModelError(nameof(vm.ClienteId), "Selecciona un cliente.");
            }

            if (!vm.LugarPendiente && string.IsNullOrWhiteSpace(vm.Direccion))
                ModelState.AddModelError(nameof(vm.Direccion), "Escribe la dirección del evento o marca «Lugar pendiente».");

            if (!vm.HorarioPendiente)
            {
                if (!vm.HoraInicio.HasValue) ModelState.AddModelError(nameof(vm.HoraInicio), "Indica la hora de inicio o marca «Horario pendiente».");
                if (!vm.DuracionHoras.HasValue) ModelState.AddModelError(nameof(vm.DuracionHoras), "Indica la duración o marca «Horario pendiente».");
            }
            else
            {
                ModelState.Remove(nameof(vm.HoraInicio));
                ModelState.Remove(nameof(vm.DuracionHoras));
            }

            if (!vm.Dia.HasValue || !vm.Mes.HasValue) return null;

            var fecha = ResolverFecha(vm.Dia, vm.Mes, vm.DiaOriginal, vm.MesOriginal, vm.AnioOriginal);
            if (fecha == null)
            {
                ModelState.AddModelError(nameof(vm.Dia), "Esa fecha no existe (revisa el día y el mes).");
                return null;
            }

            // Horario pendiente: el evento ocupa el día completo y no se revisan empalmes de horario
            if (vm.HorarioPendiente)
                return (fecha.Value, fecha.Value.AddDays(1).AddMinutes(-1));

            if (!vm.HoraInicio.HasValue || !vm.DuracionHoras.HasValue) return null;
            if (vm.DuracionHoras <= 0 || vm.DuracionHoras > 12) return null;

            var inicio = fecha.Value.Add(vm.HoraInicio.Value);
            var fin = inicio.AddMinutes((double)(vm.DuracionHoras.Value * 60));

            if (vm.Estado != EstadoContratacion.Cancelada)
            {
                var conflictos = await _agenda.BuscarConflictosAsync(inicio, fin, excluirId);
                foreach (var e in conflictos)
                {
                    // No se bloquea: se registra igual y en la agenda sale en amarillo
                    _avisosEmpalme.Add($"Choca con la presentación de {e.Contratacion.Cliente.NombreCompleto} " +
                        $"({e.Inicio.ToString("d MMM h:mm tt", Mx)} – {e.Fin.ToString("h:mm tt", Mx)}). " +
                        $"Se requiere un margen de {_agenda.MargenMinutos} min entre eventos.");
                }
            }
            return (inicio, fin);
        }

        /// <summary>Descarga todas las contrataciones en un archivo que abre Excel (CSV).</summary>
        [HttpGet]
        public async Task<IActionResult> Exportar()
        {
            var filas = await _db.Eventos
                .OrderBy(e => e.Inicio)
                .Select(e => new
                {
                    e.Inicio,
                    e.Fin,
                    e.HorarioPendiente,
                    Cliente = e.Contratacion.Cliente.NombreCompleto,
                    Telefono = e.Contratacion.Cliente.Telefono,
                    e.Lugar,
                    e.Direccion,
                    e.Ciudad,
                    e.Contratacion.Estado,
                    Precio = e.Contratacion.PrecioAcordado,
                    Pagado = e.Contratacion.Pagos.Sum(p => p.Monto)
                }).ToListAsync();

            static string C(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Fecha,Hora inicio,Hora fin,Cliente,Teléfono,Lugar,Estado,Precio,Pagado,Saldo");
            foreach (var f in filas)
            {
                var lugar = string.Join(", ", new[] { f.Lugar, f.Direccion, f.Ciudad }.Where(x => !string.IsNullOrWhiteSpace(x)));
                sb.AppendLine(string.Join(",",
                    f.Inicio.ToString("yyyy-MM-dd", inv),
                    f.HorarioPendiente ? "Pendiente" : f.Inicio.ToString("h:mm tt", Mx),
                    f.HorarioPendiente ? "" : f.Fin.ToString("h:mm tt", Mx),
                    C(f.Cliente), C(f.Telefono), C(lugar.Length == 0 ? "Pendiente" : lugar),
                    f.Estado.Nombre(),
                    f.Precio.ToString("0.00", inv), f.Pagado.ToString("0.00", inv), (f.Precio - f.Pagado).ToString("0.00", inv)));
            }
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv", $"contrataciones-{DateTime.Today:yyyy-MM-dd}.csv");
        }

        /// <summary>Quita pagos de la contratación; los documentos que los referían se conservan sin el vínculo.</summary>
        private void QuitarPagos(Contratacion c, IEnumerable<Pago> pagos)
        {
            foreach (var p in pagos.ToList())
            {
                foreach (var d in c.Documentos.Where(d => d.PagoId == p.Id)) d.PagoId = null;
                _db.Pagos.Remove(p);
            }
        }

        private async Task<Cliente> ObtenerOCrearClienteAsync(ContratacionFormViewModel vm)
        {
            if (!vm.NuevoCliente)
                return (await _db.Clientes.FindAsync(vm.ClienteId!.Value))!;

            var tel = Formato.Telefono(vm.ClienteTelefono);
            var digitos = Formato.SoloDigitos(tel);
            var existente = await _db.Clientes.FirstOrDefaultAsync(c => c.Telefono.Replace(" ", "").Replace("-", "") == digitos);
            if (existente != null) return existente; // evita clientes duplicados con el mismo teléfono

            var nuevo = new Cliente
            {
                NombreCompleto = vm.ClienteNombre!.Trim(),
                Telefono = tel,
                FechaRegistro = DateTime.Now
            };
            _db.Clientes.Add(nuevo);
            return nuevo;
        }

        private async Task CargarListasAsync(ContratacionFormViewModel vm)
        {
            vm.Clientes = await _db.Clientes.OrderBy(c => c.NombreCompleto)
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.NombreCompleto + " · " + c.Telefono })
                .ToListAsync();
        }
    }
}
