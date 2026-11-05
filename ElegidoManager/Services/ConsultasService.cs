using System.Globalization;
using System.Linq.Expressions;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElegidoManager.Services
{
    // ----------------------------------------------------------------- Modelos de la conversación
    public record PreguntaPredefinida(string Clave, string Texto, string Categoria, string Requiere, string Icono);

    public class RespuestaConsulta
    {
        public string Pregunta { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public List<string> Parrafos { get; set; } = new();
        /// <summary>exito | alerta | info | vacio</summary>
        public string Tono { get; set; } = "info";
        public List<DatoConsulta> Datos { get; set; } = new();
        public List<ItemConsulta> Items { get; set; } = new();
        public List<DiaConsulta> Dias { get; set; } = new();
        public int DesfaseCalendario { get; set; }
        public List<AccionConsulta> Acciones { get; set; } = new();
    }

    public record DatoConsulta(string Etiqueta, string Valor, string? Clase = null);

    public class ItemConsulta
    {
        public string Titulo { get; set; } = string.Empty;
        public string? Subtitulo { get; set; }
        public string? Detalle { get; set; }
        public string? Etiqueta { get; set; }
        public string? EtiquetaClase { get; set; }
        public string? Monto { get; set; }
        public string? Url { get; set; }
        public string? WhatsApp { get; set; }
        public string? FechaDia { get; set; }
        public string? FechaMes { get; set; }
    }

    public record DiaConsulta(string Fecha, int Numero, bool Libre, bool FinDeSemana, bool Pasado, string? Url);

    public record AccionConsulta(string Texto, string Url, string Icono, bool Principal = false);

    /// <summary>
    /// Motor del módulo "Consultas rápidas". NO usa inteligencia artificial:
    /// cada pregunta predefinida se traduce en consultas LINQ que EF Core ejecuta en SQL Server.
    /// </summary>
    public class ConsultasService
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        private readonly GrupoSettings _grupo;
        private readonly AgendaService _agenda;
        private IUrlHelper _url = null!;

        public ConsultasService(AppDbContext db, IOptions<GrupoSettings> grupo, AgendaService agenda)
        {
            _db = db;
            _grupo = grupo.Value;
            _agenda = agenda;
        }

        public static readonly List<PreguntaPredefinida> Preguntas = new()
        {
            new("fechas-disponibles", "¿Qué fechas tenemos disponibles?", "Agenda", "mes", "bi-calendar2-check"),
            new("fechas-ocupadas", "¿Qué fechas están ocupadas?", "Agenda", "mes", "bi-calendar2-x"),
            new("fin-de-semana", "¿Tenemos alguna presentación este fin de semana?", "Agenda", "ninguno", "bi-calendar-week"),
            new("este-viernes", "¿Qué tenemos para este viernes?", "Agenda", "ninguno", "bi-calendar-event"),
            new("este-sabado", "¿Qué tenemos para este sábado?", "Agenda", "ninguno", "bi-calendar-event"),
            new("horarios-sabado", "¿Qué horarios tenemos ocupados este sábado?", "Agenda", "ninguno", "bi-clock"),
            new("eventos-mes", "¿Qué eventos tenemos durante el mes…?", "Agenda", "mes", "bi-calendar3"),

            new("generado-finde", "¿Cuánto generé este fin de semana?", "Finanzas", "ninguno", "bi-wallet2"),
            new("generado-mes", "¿Cuánto hemos generado este mes?", "Finanzas", "ninguno", "bi-graph-up-arrow"),
            new("pendiente-cobrar", "¿Cuánto dinero tenemos pendiente de cobrar?", "Finanzas", "ninguno", "bi-hourglass-split"),
            new("clientes-deben", "¿Qué clientes todavía deben dinero?", "Finanzas", "ninguno", "bi-person-exclamation"),
            new("anticipos", "¿Cuántos anticipos hemos recibido?", "Finanzas", "ninguno", "bi-cash-coin"),

            new("proximo-sabado", "¿Quién nos contrató para el próximo sábado?", "Contrataciones", "ninguno", "bi-person-check"),
            new("quien-fecha", "¿Quién nos contrató para una fecha?", "Contrataciones", "fecha", "bi-person-vcard"),
            new("presentaciones-mes", "¿Cuántas presentaciones tenemos este mes?", "Contrataciones", "ninguno", "bi-music-note-beamed"),
            new("canceladas", "¿Qué contrataciones fueron canceladas?", "Contrataciones", "ninguno", "bi-x-octagon")
        };

        public async Task<RespuestaConsulta> ResponderAsync(string clave, DateTime? fecha, string? mes, IUrlHelper url)
        {
            _url = url;
            var m = ParsearMes(mes);
            return clave switch
            {
                "fechas-disponibles" => await FechasDisponibles(m),
                "fechas-ocupadas" => await FechasOcupadas(m),
                "fin-de-semana" => await FinDeSemana(),
                "proxima" => await Proxima(),
                "este-viernes" => await QueTenemos(DayOfWeek.Friday, "viernes"),
                "este-sabado" => await QueTenemos(DayOfWeek.Saturday, "sábado"),
                "horarios-sabado" => await HorariosOcupados(DayOfWeek.Saturday, "sábado"),
                "eventos-mes" => await EventosMes(m),
                "disponible-fecha" => await DisponibleFecha(fecha ?? DateTime.Today),
                "generado-finde" => await GeneradoFinDeSemana(),
                "generado-mes" => await GeneradoMes(),
                "pendiente-cobrar" => await PendienteCobrar(),
                "clientes-deben" => await ClientesDeben(),
                "anticipos" => await Anticipos(),
                "proximo-sabado" => await ProximoSabado(),
                "quien-fecha" => await QuienFecha(fecha ?? DateTime.Today),
                "presentaciones-mes" => await PresentacionesMes(),
                "pendientes-confirmar" => await PendientesConfirmar(),
                "canceladas" => await Canceladas(),
                _ => new RespuestaConsulta { Titulo = "No reconozco esa consulta", Tono = "alerta", Parrafos = { "Selecciona una de las preguntas disponibles." } }
            };
        }

        // =============================================================== AGENDA
        private async Task<RespuestaConsulta> FechasDisponibles(DateTime mes)
        {
            var r = new RespuestaConsulta { Pregunta = $"¿Qué fechas tenemos disponibles en {NombreMes(mes)}?" };
            var hoy = DateTime.Today;
            var fin = mes.AddMonths(1);
            if (fin <= hoy)
            {
                r.Tono = "alerta";
                r.Titulo = $"{Capital(NombreMes(mes))} ya pasó";
                r.Parrafos.Add("Selecciona un mes actual o futuro para consultar disponibilidad.");
                return r;
            }

            var ocupados = await DiasOcupados(mes, fin);
            LlenarCalendario(r, mes, ocupados);
            var libres = r.Dias.Where(d => d.Libre && !d.Pasado).ToList();
            var librasFinde = libres.Count(d => d.FinDeSemana);

            if (libres.Count == 0)
            {
                r.Tono = "alerta";
                r.Titulo = "¡Agenda llena!";
                r.Parrafos.Add($"No quedan fechas libres en {NombreMes(mes)}.");
            }
            else
            {
                r.Tono = "exito";
                r.Titulo = $"Tenemos {libres.Count} fecha(s) disponibles en {NombreMes(mes)}";
                r.Parrafos.Add($"De ellas, {librasFinde} caen en viernes, sábado o domingo. Las fechas libres aparecen resaltadas en dorado; toca una para registrar una contratación.");
            }
            r.Datos.Add(new("Días libres", libres.Count.ToString()));
            r.Datos.Add(new("Fines de semana libres", librasFinde.ToString()));
            r.Datos.Add(new("Días ocupados", ocupados.Count.ToString()));
            return r;
        }

        private async Task<RespuestaConsulta> FechasOcupadas(DateTime mes)
        {
            var r = new RespuestaConsulta { Pregunta = $"¿Qué fechas están ocupadas en {NombreMes(mes)}?" };
            var fin = mes.AddMonths(1);
            var eventos = await Eventos(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= mes && e.Inicio < fin);
            LlenarCalendario(r, mes, eventos.Select(e => e.Inicio.Date).ToHashSet());

            if (eventos.Count == 0)
            {
                r.Tono = "vacio";
                r.Titulo = $"No hay fechas ocupadas en {NombreMes(mes)}";
                r.Parrafos.Add("Todo el mes está libre para recibir contrataciones.");
                return r;
            }
            var dias = eventos.Select(e => e.Inicio.Date).Distinct().Count();
            r.Tono = "info";
            r.Titulo = $"{dias} día(s) ocupados en {NombreMes(mes)}";
            r.Parrafos.Add($"Hay {eventos.Count} presentación(es) registradas. Estos son los detalles:");
            r.Items = eventos.Select(ItemEvento).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> FinDeSemana()
        {
            var hoy = DateTime.Today;
            // Viernes de esta semana (si hoy es sáb/dom, el viernes que acaba de pasar)
            var dow = (int)hoy.DayOfWeek; // domingo = 0
            DateTime viernes = dow switch
            {
                0 => hoy.AddDays(-2),
                6 => hoy.AddDays(-1),
                _ => hoy.AddDays(5 - dow)
            };
            var lunes = viernes.AddDays(3);
            var desde = viernes < hoy ? hoy : viernes;
            var ahora = DateTime.Now;

            var r = new RespuestaConsulta { Pregunta = "¿Tenemos alguna presentación este fin de semana?" };
            var eventos = await Eventos(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= desde && e.Inicio < lunes && e.Fin >= ahora);
            var rango = $"del viernes {viernes.Day} al domingo {viernes.AddDays(2).ToString("d 'de' MMMM", Mx)}";

            if (eventos.Count == 0)
            {
                r.Tono = "vacio";
                r.Titulo = "Este fin de semana está libre";
                r.Parrafos.Add($"No hay presentaciones registradas {rango}.");
                r.Acciones.Add(new("Registrar contratación", _url.Action("Crear", "Contrataciones", new { fecha = viernes.AddDays(1).ToString("yyyy-MM-dd") })!, "bi-plus-lg", true));
                return r;
            }
            r.Tono = "exito";
            r.Titulo = "Estas son las presentaciones programadas:";
            r.Parrafos.Add($"Fin de semana {rango}.");
            r.Items = eventos.Select(ItemEvento).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> Proxima()
        {
            var ahora = DateTime.Now;
            var r = new RespuestaConsulta { Pregunta = "¿Cuál es nuestra próxima presentación?" };
            var e = (await Eventos(x => x.Contratacion.Estado != EstadoContratacion.Cancelada && x.Fin >= ahora, 1)).FirstOrDefault();
            if (e == null)
            {
                r.Tono = "vacio";
                r.Titulo = "No hay presentaciones próximas";
                r.Parrafos.Add("Todavía no hay eventos registrados a futuro.");
                r.Acciones.Add(new("Registrar contratación", _url.Action("Crear", "Contrataciones")!, "bi-plus-lg", true));
                return r;
            }

            var dias = (e.Inicio.Date - DateTime.Today).Days;
            var cuando = e.Inicio <= ahora ? "¡Está en curso ahora mismo!" : dias switch
            {
                0 => "¡Es hoy!",
                1 => "Es mañana.",
                _ => $"Faltan {dias} días."
            };
            r.Tono = "exito";
            r.Titulo = e.Cliente;
            r.Parrafos.Add($"{Capital(e.Inicio.ToString("dddd d 'de' MMMM", Mx))}, de {Hora(e.Inicio)} a {Hora(e.Fin)} en {Ubicacion(e)}. {cuando}");
            r.Datos.Add(new("Estado", e.Estado.Nombre(), e.Estado.Clase()));
            r.Datos.Add(new("Precio", e.Precio.ToString("C0", Mx)));
            r.Datos.Add(new("Saldo", e.Saldo.ToString("C0", Mx), e.Saldo > 0 ? "text-amarillo" : "text-verde"));
            r.Acciones.Add(new("Ver contratación", _url.Action("Detalle", "Contrataciones", new { id = e.ContratacionId })!, "bi-box-arrow-up-right", true));
            r.Acciones.Add(new("WhatsApp al cliente", WhatsApp.Enlace(e.Telefono), "bi-whatsapp"));
            return r;
        }

        private async Task<RespuestaConsulta> EventosMes(DateTime mes)
        {
            var r = new RespuestaConsulta { Pregunta = $"¿Qué eventos tenemos durante {NombreMes(mes)}?" };
            var fin = mes.AddMonths(1);
            var todos = await Eventos(e => e.Inicio >= mes && e.Inicio < fin);
            var activos = todos.Where(e => e.Estado != EstadoContratacion.Cancelada).ToList();
            var canceladas = todos.Count - activos.Count;

            if (activos.Count == 0)
            {
                r.Tono = "vacio";
                r.Titulo = $"No hay eventos en {NombreMes(mes)}";
                r.Parrafos.Add(canceladas > 0 ? $"Solo hay {canceladas} contratación(es) cancelada(s) en ese mes." : "Aún no se ha registrado ninguna presentación para ese mes.");
                return r;
            }
            r.Tono = "info";
            r.Titulo = $"{activos.Count} evento(s) en {NombreMes(mes)}";
            if (canceladas > 0) r.Parrafos.Add($"Además hay {canceladas} cancelada(s) que no se muestran.");
            r.Items = activos.Select(ItemEvento).ToList();
            r.Datos.Add(new("Presentaciones", activos.Count.ToString()));
            r.Datos.Add(new("Valor contratado", activos.Sum(e => e.Precio).ToString("C0", Mx)));
            return r;
        }

        private async Task<RespuestaConsulta> DisponibleFecha(DateTime fecha)
        {
            var d = fecha.Date;
            var r = new RespuestaConsulta { Pregunta = $"¿Tenemos disponible el {d.ToString("d 'de' MMMM", Mx)}?" };
            var sig = d.AddDays(1);
            var eventos = await Eventos(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= d && e.Inicio < sig);
            var pasado = d < DateTime.Today;

            if (eventos.Count == 0)
            {
                r.Tono = pasado ? "vacio" : "exito";
                r.Titulo = pasado ? "Esa fecha ya pasó" : "¡La fecha está disponible!";
                r.Parrafos.Add($"No existen presentaciones registradas para el {d.ToString("dddd d 'de' MMMM 'de' yyyy", Mx)}.");
                if (!pasado)
                {
                    r.Parrafos.Add("¿Deseas registrar una nueva contratación?");
                    r.Acciones.Add(new("Sí, registrar contratación", _url.Action("Crear", "Contrataciones", new { fecha = d.ToString("yyyy-MM-dd") })!, "bi-plus-lg", true));
                }
                return r;
            }

            r.Tono = "alerta";
            r.Titulo = $"Ese día ya tenemos {eventos.Count} presentación(es)";
            r.Parrafos.Add($"El {d.ToString("dddd d 'de' MMMM", Mx)} está ocupado en los siguientes horarios. Si el cliente necesita otro horario, verifica que no se empalme considerando el traslado:");
            r.Items = eventos.Select(ItemEvento).ToList();
            if (!pasado)
                r.Acciones.Add(new("Registrar en otro horario", _url.Action("Crear", "Contrataciones", new { fecha = d.ToString("yyyy-MM-dd") })!, "bi-clock"));
            return r;
        }

        // =============================================================== ESTE VIERNES / SÁBADO
        /// <summary>Próximo día de la semana indicado (hoy, si hoy es ese día).</summary>
        private static DateTime ProximoDia(DayOfWeek dia)
        {
            var hoy = DateTime.Today;
            return hoy.AddDays(((int)dia - (int)hoy.DayOfWeek + 7) % 7);
        }

        private async Task<RespuestaConsulta> QueTenemos(DayOfWeek diaSemana, string nombreDia)
        {
            var d = ProximoDia(diaSemana);
            var r = await QuienFecha(d);
            r.Pregunta = $"¿Qué tenemos para este {nombreDia}?";
            if (r.Items.Count > 0)
                r.Titulo = r.Items.Count == 1
                    ? $"Este {nombreDia} {d.Day} tenemos 1 presentación"
                    : $"Este {nombreDia} {d.Day} tenemos {r.Items.Count} presentaciones";
            else
                r.Titulo = $"Este {nombreDia} {d.ToString("d 'de' MMMM", Mx)} no tenemos nada";
            return r;
        }

        private async Task<RespuestaConsulta> HorariosOcupados(DayOfWeek diaSemana, string nombreDia)
        {
            var d = ProximoDia(diaSemana);
            var sig = d.AddDays(1);
            var r = new RespuestaConsulta { Pregunta = $"¿Qué horarios tenemos ocupados este {nombreDia}?" };
            var eventos = await Eventos(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= d && e.Inicio < sig);

            if (eventos.Count == 0)
            {
                r.Tono = "exito";
                r.Titulo = $"Este {nombreDia} {d.ToString("d 'de' MMMM", Mx)} está libre todo el día";
                r.Parrafos.Add("No hay ninguna presentación registrada.");
                r.Acciones.Add(new("Registrar contratación", _url.Action("Crear", "Contrataciones", new { fecha = d.ToString("yyyy-MM-dd") })!, "bi-plus-lg", true));
                return r;
            }

            var margen = TimeSpan.FromMinutes(_agenda.MargenMinutos);
            r.Tono = "alerta";
            r.Titulo = $"Horarios ocupados este {nombreDia} {d.ToString("d 'de' MMMM", Mx)}";
            r.Datos = eventos.Select(e => new DatoConsulta(e.Cliente, e.HorarioTexto, "text-amarillo")).ToList();

            // Huecos libres entre las 10:00 a. m. y las 3:00 a. m., respetando el margen de traslado
            var ventanaIni = d.AddHours(10);
            var ventanaFin = d.AddDays(1).AddHours(3);
            var bloqueos = eventos.Where(e => !e.HorarioPendiente).Select(e => (Ini: e.Inicio - margen, Fin: e.Fin + margen)).OrderBy(b => b.Ini).ToList();
            if (eventos.Any(e => e.HorarioPendiente))
                r.Parrafos.Add("Ojo: hay una contratación con horario pendiente ese día; confírmala antes de aceptar otra.");
            var libres = new List<string>();
            var cursor = ventanaIni;
            foreach (var b in bloqueos)
            {
                if (b.Ini - cursor >= TimeSpan.FromHours(1)) libres.Add($"de {Hora(cursor)} a {Hora(b.Ini)}");
                if (b.Fin > cursor) cursor = b.Fin;
            }
            if (ventanaFin - cursor >= TimeSpan.FromHours(1)) libres.Add($"de {Hora(cursor)} a {Hora(ventanaFin)}");

            r.Parrafos.Add(libres.Count > 0
                ? $"Considerando {_agenda.MargenMinutos} min de traslado, todavía podrías tocar: {string.Join("; ", libres)}."
                : "Considerando el tiempo de traslado, ya no queda espacio para otra presentación ese día.");
            r.Items = eventos.Select(ItemEvento).ToList();
            return r;
        }

        // =============================================================== FINANZAS
        /// <summary>
        /// Lo que ganaste el fin de semana (viernes a domingo). De lunes a jueves toma el fin de semana que pasó;
        /// de viernes a domingo, el actual. Misma fórmula que Finanzas: precio − anticipo − pagos a integrantes − gasolina − otros.
        /// </summary>
        private async Task<RespuestaConsulta> GeneradoFinDeSemana()
        {
            var hoy = DateTime.Today;
            var viernes = hoy.AddDays(-(((int)hoy.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7));
            var lunes = viernes.AddDays(3);
            var ahora = DateTime.Now;

            var eventos = await _db.Eventos
                .Where(e => e.Inicio >= viernes && e.Inicio < lunes && e.Contratacion.Estado != EstadoContratacion.Cancelada)
                .OrderBy(e => e.Inicio)
                .Select(e => new
                {
                    e.ContratacionId,
                    e.Inicio,
                    e.Fin,
                    e.HorarioPendiente,
                    Cliente = e.Contratacion.Cliente.NombreCompleto,
                    e.Contratacion.PrecioAcordado,
                    Anticipo = e.Contratacion.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto),
                    Pagos = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes).Sum(g => g.Monto),
                    Gasolina = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaGasolina).Sum(g => g.Monto),
                    Otros = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaOtro && g.Notas == Catalogos.MarcaPagosEvento).Sum(g => g.Monto),
                    Registrado = e.Contratacion.Gastos.Any(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes || g.Categoria == Catalogos.CategoriaGasolina || g.Notas == Catalogos.MarcaPagosEvento)
                })
                .ToListAsync();

            var rango = $"{viernes.ToString("dddd d", Mx)} al {lunes.AddDays(-1).ToString("dddd d 'de' MMMM", Mx)}";
            var r = new RespuestaConsulta { Pregunta = "¿Cuánto generé este fin de semana?" };

            if (eventos.Count == 0)
            {
                r.Tono = "vacio";
                r.Titulo = "No hubo presentaciones ese fin de semana";
                r.Parrafos.Add($"No hay eventos registrados del {rango}.");
                return r;
            }

            decimal ganancia = 0, anticipos = 0, contratado = 0;
            int conGanancia = 0, sinRegistrar = 0, porTocar = 0;
            foreach (var e in eventos)
            {
                contratado += e.PrecioAcordado;
                anticipos += e.Anticipo;
                var item = new ItemConsulta
                {
                    Titulo = e.Cliente,
                    Subtitulo = Textos.Horario(e.Inicio, e.Fin, e.HorarioPendiente),
                    FechaDia = e.Inicio.Day.ToString(),
                    FechaMes = e.Inicio.ToString("ddd", Mx),
                    Url = _url.Action("Detalle", "Contrataciones", new { id = e.ContratacionId })
                };
                if (e.Fin >= ahora)
                {
                    porTocar++;
                    item.Etiqueta = "Aún no se toca";
                    item.EtiquetaClase = "text-secundario";
                    item.Monto = e.PrecioAcordado.ToString("C0", Mx);
                }
                else if (!e.Registrado)
                {
                    sinRegistrar++;
                    item.Etiqueta = "Faltan los pagos del evento";
                    item.EtiquetaClase = "text-amarillo";
                    item.Monto = e.PrecioAcordado.ToString("C0", Mx);
                    item.Url = _url.Action("Evento", "Finanzas", new { id = e.ContratacionId });
                }
                else
                {
                    var g = e.PrecioAcordado - e.Anticipo - e.Pagos - e.Gasolina - e.Otros;
                    ganancia += g;
                    conGanancia++;
                    item.Etiqueta = "Tu ganancia";
                    item.EtiquetaClase = "text-verde";
                    item.Monto = g.ToString("C0", Mx);
                }
                r.Items.Add(item);
            }

            var total = ganancia + anticipos;
            r.Tono = total > 0 ? "exito" : "info";
            r.Titulo = $"Este fin de semana generaste {total.ToString("C0", Mx)}";
            r.Parrafos.Add($"Del {rango}: {eventos.Count} presentación(es). Es tu ganancia de los eventos ya trabajados más los anticipos que te dieron por esos eventos.");
            if (sinRegistrar > 0)
                r.Parrafos.Add($"{sinRegistrar} evento(s) todavía no tienen registrados los pagos a integrantes; registra los pagos para que su ganancia se sume.");
            if (porTocar > 0)
                r.Parrafos.Add($"{porTocar} evento(s) todavía no se tocan; su ganancia se suma cuando terminen y registres los pagos.");

            r.Datos.Add(new("Tu ganancia", ganancia.ToString("C0", Mx), "text-oro"));
            r.Datos.Add(new("Anticipos", anticipos.ToString("C0", Mx), "text-verde"));
            r.Datos.Add(new("Total generado", total.ToString("C0", Mx), "text-oro"));
            r.Datos.Add(new("Valor contratado", contratado.ToString("C0", Mx)));
            r.Acciones.Add(new("Ver pagos por evento", _url.Action("PorEvento", "Finanzas")!, "bi-cash-stack"));
            return r;
        }

        private async Task<RespuestaConsulta> GeneradoMes()
        {
            var hoy = DateTime.Today;
            var ini = new DateTime(hoy.Year, hoy.Month, 1);
            var fin = ini.AddMonths(1);
            var ant = ini.AddMonths(-1);

            var ingresos = await _db.Pagos.Where(p => p.Fecha >= ini && p.Fecha < fin).SumAsync(p => p.Monto);
            var numPagos = await _db.Pagos.CountAsync(p => p.Fecha >= ini && p.Fecha < fin);
            var gastos = await _db.Gastos.Where(g => g.Fecha >= ini && g.Fecha < fin).SumAsync(g => g.Monto);
            var ingresosAnt = await _db.Pagos.Where(p => p.Fecha >= ant && p.Fecha < ini).SumAsync(p => p.Monto);

            var r = new RespuestaConsulta { Pregunta = "¿Cuánto hemos generado este mes?" };
            r.Tono = ingresos > 0 ? "exito" : "vacio";
            r.Titulo = ingresos > 0
                ? $"En {NombreMes(ini)} llevamos {ingresos.ToString("C", Mx)} cobrados"
                : $"Aún no hay ingresos registrados en {NombreMes(ini)}";
            r.Parrafos.Add($"Se registraron {numPagos} pago(s) entre anticipos, abonos y liquidaciones.");
            if (ingresosAnt > 0)
            {
                var dif = ingresos - ingresosAnt;
                r.Parrafos.Add(dif >= 0
                    ? $"Vamos {dif.ToString("C0", Mx)} arriba del mes pasado ({ingresosAnt.ToString("C0", Mx)})."
                    : $"Vamos {(-dif).ToString("C0", Mx)} abajo del mes pasado ({ingresosAnt.ToString("C0", Mx)}).");
            }
            r.Datos.Add(new("Ingresos", ingresos.ToString("C0", Mx), "text-verde"));
            r.Datos.Add(new("Gastos", gastos.ToString("C0", Mx), "text-rojo"));
            r.Datos.Add(new("Ganancia", (ingresos - gastos).ToString("C0", Mx), "text-oro"));
            r.Acciones.Add(new("Ver finanzas del mes", _url.Action("Index", "Finanzas")!, "bi-cash-stack"));
            return r;
        }

        private async Task<List<(int Id, string Cliente, string Telefono, string Tipo, DateTime? Fecha, decimal Precio, decimal Pagado)>> CuentasConSaldo()
        {
            var datos = await _db.Contrataciones
                .Where(c => c.Estado != EstadoContratacion.Cancelada)
                .Select(c => new
                {
                    c.Id,
                    Cliente = c.Cliente.NombreCompleto,
                    c.Cliente.Telefono,
                    c.TipoEvento,
                    Fecha = c.Evento != null ? (DateTime?)c.Evento.Inicio : null,
                    c.PrecioAcordado,
                    Pagado = c.Pagos.Sum(p => p.Monto)
                }).ToListAsync();
            return datos.Where(x => x.PrecioAcordado - x.Pagado > 0)
                .OrderBy(x => x.Fecha)
                .Select(x => (x.Id, x.Cliente, x.Telefono, x.TipoEvento, x.Fecha, x.PrecioAcordado, x.Pagado))
                .ToList();
        }

        private async Task<RespuestaConsulta> PendienteCobrar()
        {
            var cuentas = await CuentasConSaldo();
            var r = new RespuestaConsulta { Pregunta = "¿Cuánto dinero tenemos pendiente de cobrar?" };
            if (cuentas.Count == 0)
            {
                r.Tono = "exito";
                r.Titulo = "¡No hay saldos pendientes!";
                r.Parrafos.Add("Todas las contrataciones activas están liquidadas.");
                return r;
            }
            var total = cuentas.Sum(x => x.Precio - x.Pagado);
            var ahora = DateTime.Now;
            var vencidas = cuentas.Where(x => x.Fecha.HasValue && x.Fecha < ahora).ToList();

            r.Tono = "alerta";
            r.Titulo = $"Tenemos {total.ToString("C", Mx)} pendientes de cobrar";
            r.Parrafos.Add($"Corresponde a {cuentas.Count} contratación(es) activas con saldo.");
            if (vencidas.Count > 0)
                r.Parrafos.Add($"Ojo: {vencidas.Count} de ellas son de eventos que ya se realizaron ({vencidas.Sum(x => x.Precio - x.Pagado).ToString("C0", Mx)}).");
            r.Datos.Add(new("Total por cobrar", total.ToString("C0", Mx), "text-amarillo"));
            r.Datos.Add(new("Contrataciones", cuentas.Count.ToString()));
            r.Datos.Add(new("De eventos pasados", vencidas.Sum(x => x.Precio - x.Pagado).ToString("C0", Mx), vencidas.Count > 0 ? "text-rojo" : null));
            r.Items = cuentas.Take(12).Select(x => new ItemConsulta
            {
                Titulo = x.Cliente,
                Subtitulo = x.Fecha.HasValue ? x.Fecha.Value.ToString("d MMM yyyy", Mx) : "sin fecha",
                Monto = (x.Precio - x.Pagado).ToString("C0", Mx),
                Detalle = $"Pagado {x.Pagado.ToString("C0", Mx)} de {x.Precio.ToString("C0", Mx)}",
                Url = _url.Action("Detalle", "Contrataciones", new { id = x.Id }),
                FechaDia = x.Fecha?.Day.ToString(),
                FechaMes = x.Fecha?.ToString("MMM", Mx)
            }).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> ClientesDeben()
        {
            var cuentas = await CuentasConSaldo();
            var r = new RespuestaConsulta { Pregunta = "¿Qué clientes todavía deben dinero?" };
            if (cuentas.Count == 0)
            {
                r.Tono = "exito";
                r.Titulo = "Ningún cliente tiene adeudos";
                r.Parrafos.Add("Todas las cuentas activas están al corriente.");
                return r;
            }
            var porCliente = cuentas.GroupBy(x => new { x.Cliente, x.Telefono })
                .Select(g => new
                {
                    g.Key.Cliente,
                    g.Key.Telefono,
                    Saldo = g.Sum(x => x.Precio - x.Pagado),
                    Eventos = g.Count(),
                    Proximo = g.Min(x => x.Fecha),
                    PrimerId = g.First().Id
                })
                .OrderByDescending(x => x.Saldo).ToList();

            r.Tono = "alerta";
            r.Titulo = $"{porCliente.Count} cliente(s) con saldo pendiente";
            r.Parrafos.Add("Puedes enviarles un recordatorio por WhatsApp con un solo toque:");
            r.Items = porCliente.Select(x =>
            {
                var nombre = x.Cliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? x.Cliente;
                var msg = $"Hola {nombre}, le saluda {_grupo.Nombre}. Le recordamos amablemente que tiene un saldo pendiente de {x.Saldo.ToString("C", Mx)} " +
                          $"por la contratación del grupo. Quedamos atentos, ¡gracias!";
                return new ItemConsulta
                {
                    Titulo = x.Cliente,
                    Subtitulo = $"{x.Eventos} contratación(es) · {Formato.Telefono(x.Telefono)}",
                    Monto = x.Saldo.ToString("C0", Mx),
                    Url = _url.Action("Detalle", "Contrataciones", new { id = x.PrimerId }),
                    WhatsApp = WhatsApp.Enlace(x.Telefono, msg)
                };
            }).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> Anticipos()
        {
            var hoy = DateTime.Today;
            var ini = new DateTime(hoy.Year, hoy.Month, 1);
            var fin = ini.AddMonths(1);
            var anticipos = _db.Pagos.Where(p => p.Tipo == TipoPago.Anticipo);

            var numMes = await anticipos.CountAsync(p => p.Fecha >= ini && p.Fecha < fin);
            var sumMes = await anticipos.Where(p => p.Fecha >= ini && p.Fecha < fin).SumAsync(p => p.Monto);
            var numTotal = await anticipos.CountAsync();
            var sumTotal = await anticipos.SumAsync(p => p.Monto);

            var ultimos = await anticipos.OrderByDescending(p => p.Fecha).Take(6)
                .Select(p => new { p.ContratacionId, p.Fecha, p.Monto, p.Metodo, Cliente = p.Contratacion.Cliente.NombreCompleto, p.Contratacion.TipoEvento })
                .ToListAsync();

            var r = new RespuestaConsulta { Pregunta = "¿Cuántos anticipos hemos recibido?" };
            if (numTotal == 0)
            {
                r.Tono = "vacio";
                r.Titulo = "Todavía no se han registrado anticipos";
                r.Parrafos.Add("Los anticipos se registran al crear una contratación o como el primer pago de una.");
                return r;
            }
            r.Tono = "exito";
            r.Titulo = $"Este mes hemos recibido {numMes} anticipo(s) por {sumMes.ToString("C", Mx)}";
            r.Parrafos.Add($"En total, el sistema registra {numTotal} anticipo(s) que suman {sumTotal.ToString("C", Mx)}. Los más recientes:");
            r.Datos.Add(new("Este mes", numMes.ToString()));
            r.Datos.Add(new("Monto del mes", sumMes.ToString("C0", Mx), "text-verde"));
            r.Datos.Add(new("Histórico", sumTotal.ToString("C0", Mx)));
            r.Items = ultimos.Select(p => new ItemConsulta
            {
                Titulo = p.Cliente,
                Subtitulo = p.Metodo.Nombre(),
                Monto = p.Monto.ToString("C0", Mx),
                Url = _url.Action("Detalle", "Contrataciones", new { id = p.ContratacionId }),
                FechaDia = p.Fecha.Day.ToString(),
                FechaMes = p.Fecha.ToString("MMM", Mx)
            }).ToList();
            return r;
        }

        // =============================================================== CONTRATACIONES
        private async Task<RespuestaConsulta> ProximoSabado()
        {
            var hoy = DateTime.Today;
            var sabado = hoy.AddDays(((int)DayOfWeek.Saturday - (int)hoy.DayOfWeek + 7) % 7);
            var r = await QuienFecha(sabado);
            r.Pregunta = "¿Quién nos contrató para el próximo sábado?";
            return r;
        }

        private async Task<RespuestaConsulta> QuienFecha(DateTime fecha)
        {
            var d = fecha.Date;
            var sig = d.AddDays(1);
            var r = new RespuestaConsulta { Pregunta = $"¿Quién nos contrató para el {d.ToString("dddd d 'de' MMMM", Mx)}?" };
            var eventos = await Eventos(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= d && e.Inicio < sig);
            if (eventos.Count == 0)
            {
                r.Tono = "vacio";
                r.Titulo = $"Nadie nos ha contratado para el {d.ToString("dddd d 'de' MMMM", Mx)}";
                r.Parrafos.Add(d >= DateTime.Today ? "La fecha sigue libre." : "No hubo presentaciones ese día.");
                if (d >= DateTime.Today)
                    r.Acciones.Add(new("Registrar contratación", _url.Action("Crear", "Contrataciones", new { fecha = d.ToString("yyyy-MM-dd") })!, "bi-plus-lg", true));
                return r;
            }
            r.Tono = "exito";
            r.Titulo = eventos.Count == 1
                ? $"Nos contrató {eventos[0].Cliente}"
                : $"Tenemos {eventos.Count} contrataciones ese día";
            r.Parrafos.Add($"Para el {d.ToString("dddd d 'de' MMMM 'de' yyyy", Mx)}:");
            r.Items = eventos.Select(ItemEvento).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> PresentacionesMes()
        {
            var hoy = DateTime.Today;
            var ini = new DateTime(hoy.Year, hoy.Month, 1);
            var fin = ini.AddMonths(1);
            var ahora = DateTime.Now;
            var eventos = await Eventos(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= ini && e.Inicio < fin);

            var r = new RespuestaConsulta { Pregunta = "¿Cuántas presentaciones tenemos este mes?" };
            if (eventos.Count == 0)
            {
                r.Tono = "vacio";
                r.Titulo = $"No hay presentaciones en {NombreMes(ini)}";
                r.Parrafos.Add("¡Buen momento para promocionar al grupo!");
                return r;
            }
            var realizadas = eventos.Count(e => e.Fin < ahora);
            r.Tono = "info";
            r.Titulo = $"Tenemos {eventos.Count} presentación(es) en {NombreMes(ini)}";
            r.Parrafos.Add($"Ya se realizaron {realizadas} y faltan {eventos.Count - realizadas}.");
            r.Datos.Add(new("Ya realizadas", realizadas.ToString()));
            r.Datos.Add(new("Por realizar", (eventos.Count - realizadas).ToString()));
            r.Items = eventos.Where(e => e.Fin >= ahora).Select(ItemEvento).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> PendientesConfirmar()
        {
            var ahora = DateTime.Now;
            var eventos = await Eventos(e => e.Contratacion.Estado == EstadoContratacion.Pendiente && e.Fin >= ahora);
            var r = new RespuestaConsulta { Pregunta = "¿Qué eventos están pendientes de confirmar?" };
            if (eventos.Count == 0)
            {
                r.Tono = "exito";
                r.Titulo = "No hay eventos pendientes de confirmar";
                r.Parrafos.Add("Todas las próximas presentaciones están confirmadas.");
                return r;
            }
            r.Tono = "alerta";
            r.Titulo = $"{eventos.Count} evento(s) pendientes de confirmar";
            r.Parrafos.Add("Te sugiero contactar a estos clientes para confirmar:");
            r.Items = eventos.Select(e =>
            {
                var item = ItemEvento(e);
                var nombre = e.Cliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? e.Cliente;
                item.WhatsApp = WhatsApp.Enlace(e.Telefono,
                    $"Hola {nombre}, le saluda {_grupo.Nombre}. Queremos confirmar su evento del {e.Inicio.ToString("dddd d 'de' MMMM", Mx)} " +
                    $"de {Hora(e.Inicio)} a {Hora(e.Fin)}. ¿Seguimos en pie? ¡Gracias!");
                return item;
            }).ToList();
            return r;
        }

        private async Task<RespuestaConsulta> Canceladas()
        {
            var datos = await _db.Contrataciones
                .Where(c => c.Estado == EstadoContratacion.Cancelada)
                .OrderByDescending(c => c.FechaActualizacion ?? c.FechaRegistro)
                .Take(20)
                .Select(c => new
                {
                    c.Id,
                    Cliente = c.Cliente.NombreCompleto,
                    c.TipoEvento,
                    Fecha = c.Evento != null ? (DateTime?)c.Evento.Inicio : null,
                    c.MotivoCancelacion,
                    Pagado = c.Pagos.Sum(p => p.Monto)
                }).ToListAsync();
            var total = await _db.Contrataciones.CountAsync(c => c.Estado == EstadoContratacion.Cancelada);

            var r = new RespuestaConsulta { Pregunta = "¿Qué contrataciones fueron canceladas?" };
            if (total == 0)
            {
                r.Tono = "exito";
                r.Titulo = "No hay contrataciones canceladas";
                r.Parrafos.Add("¡Excelente! Ningún cliente ha cancelado.");
                return r;
            }
            r.Tono = "info";
            r.Titulo = $"{total} contratación(es) canceladas";
            if (total > datos.Count) r.Parrafos.Add($"Se muestran las {datos.Count} más recientes.");
            r.Items = datos.Select(c => new ItemConsulta
            {
                Titulo = c.Cliente,
                Subtitulo = c.Fecha.HasValue ? c.Fecha.Value.ToString("d MMM yyyy", Mx) : "sin fecha",
                Detalle = string.IsNullOrEmpty(c.MotivoCancelacion) ? null : "Motivo: " + c.MotivoCancelacion,
                Etiqueta = "Cancelada",
                EtiquetaClase = "estado-cancelada",
                Monto = c.Pagado > 0 ? "Anticipo retenido " + c.Pagado.ToString("C0", Mx) : null,
                Url = _url.Action("Detalle", "Contrataciones", new { id = c.Id }),
                FechaDia = c.Fecha?.Day.ToString(),
                FechaMes = c.Fecha?.ToString("MMM", Mx)
            }).ToList();
            return r;
        }

        // =============================================================== AUXILIARES
        private async Task<List<EventoResumen>> Eventos(Expression<Func<Evento, bool>> filtro, int? limite = null)
        {
            var q = _db.Eventos.Where(filtro).OrderBy(e => e.Inicio).AsQueryable();
            if (limite.HasValue) q = q.Take(limite.Value);
            return await q.Select(e => new EventoResumen
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
        }

        private async Task<HashSet<DateTime>> DiasOcupados(DateTime desde, DateTime hasta)
        {
            var dias = await _db.Eventos
                .Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= desde && e.Inicio < hasta)
                .Select(e => e.Inicio.Date).Distinct().ToListAsync();
            return dias.ToHashSet();
        }

        private void LlenarCalendario(RespuestaConsulta r, DateTime mes, HashSet<DateTime> ocupados)
        {
            var hoy = DateTime.Today;
            // Lunes = 0 ... Domingo = 6
            r.DesfaseCalendario = ((int)mes.DayOfWeek + 6) % 7;
            for (var d = mes; d < mes.AddMonths(1); d = d.AddDays(1))
            {
                var libre = !ocupados.Contains(d);
                var pasado = d < hoy;
                var finde = d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
                var url = libre && !pasado ? _url.Action("Crear", "Contrataciones", new { fecha = d.ToString("yyyy-MM-dd") }) : null;
                r.Dias.Add(new DiaConsulta(d.ToString("yyyy-MM-dd"), d.Day, libre, finde, pasado, url));
            }
        }

        private ItemConsulta ItemEvento(EventoResumen e) => new()
        {
            Titulo = e.Cliente,
            Subtitulo = $"{Capital(e.Inicio.ToString("dddd", Mx))} · {e.HorarioTexto} · {Ubicacion(e)}",
            Detalle = e.Estado == EstadoContratacion.Cancelada ? null
                : e.Saldo > 0 ? $"Saldo pendiente {e.Saldo.ToString("C0", Mx)}" : "Liquidado",
            Etiqueta = e.Estado == EstadoContratacion.Cancelada ? "Cancelada" : null,
            EtiquetaClase = e.Estado == EstadoContratacion.Cancelada ? "estado-cancelada" : null,
            Url = _url.Action("Detalle", "Contrataciones", new { id = e.ContratacionId }),
            FechaDia = e.Inicio.Day.ToString(),
            FechaMes = e.Inicio.ToString("MMM", Mx)
        };

        private static string Ubicacion(EventoResumen e) => e.Ubicacion;
        private static string Hora(DateTime d) => d.ToString("h:mm tt", Mx);
        private static string NombreMes(DateTime m) => m.Year == DateTime.Today.Year ? m.ToString("MMMM", Mx) : m.ToString("MMMM 'de' yyyy", Mx);
        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Mx) + s.Substring(1);

        private static DateTime ParsearMes(string? mes)
        {
            if (!string.IsNullOrEmpty(mes) &&
                DateTime.TryParseExact(mes, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                return m;
            var hoy = DateTime.Today;
            return new DateTime(hoy.Year, hoy.Month, 1);
        }
    }
}
