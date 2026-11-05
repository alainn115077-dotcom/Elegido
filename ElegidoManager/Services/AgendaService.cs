using ElegidoManager.Data;
using ElegidoManager.Models;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Services
{
    public class GrupoSettings
    {
        public string Nombre { get; set; } = "Grupo Elegido Sierreño";
        public string Representante { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Facebook { get; set; } = string.Empty;
    }

    public class AgendaService
    {
        private readonly AppDbContext _db;
        private readonly int _margenMinutos;

        public AgendaService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _margenMinutos = config.GetValue<int?>("Agenda:MargenTrasladoMinutos") ?? 30;
        }

        public int MargenMinutos => _margenMinutos;

        /// <summary>Convierte fecha + horas en un rango real. Si la hora final es menor o igual, el evento termina al día siguiente.</summary>
        public static (DateTime Inicio, DateTime Fin) CalcularRango(DateTime fecha, TimeSpan horaInicio, TimeSpan horaFin)
        {
            var inicio = fecha.Date.Add(horaInicio);
            var fin = fecha.Date.Add(horaFin);
            if (fin <= inicio) fin = fin.AddDays(1);
            return (inicio, fin);
        }

        /// <summary>
        /// Busca eventos no cancelados que se empalmen con el rango indicado,
        /// considerando un margen de traslado antes y después.
        /// </summary>
        public async Task<List<Evento>> BuscarConflictosAsync(DateTime inicio, DateTime fin, int? excluirContratacionId = null)
        {
            var margen = TimeSpan.FromMinutes(_margenMinutos);
            var desde = inicio - margen;
            var hasta = fin + margen;

            return await _db.Eventos
                .Include(e => e.Contratacion).ThenInclude(c => c.Cliente)
                .Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada
                            && !e.HorarioPendiente
                            && (excluirContratacionId == null || e.ContratacionId != excluirContratacionId)
                            && e.Inicio < hasta && desde < e.Fin)
                .OrderBy(e => e.Inicio)
                .ToListAsync();
        }

        /// <summary>Eventos no cancelados que inician en el día indicado.</summary>
        public async Task<List<Evento>> EventosDelDiaAsync(DateTime dia)
        {
            var d = dia.Date;
            var sig = d.AddDays(1);
            return await _db.Eventos
                .Include(e => e.Contratacion).ThenInclude(c => c.Cliente)
                .Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= d && e.Inicio < sig)
                .OrderBy(e => e.Inicio)
                .ToListAsync();
        }

        /// <summary>Días (sin hora) ocupados por eventos no cancelados en el rango [desde, hasta).</summary>
        public async Task<HashSet<DateTime>> DiasOcupadosAsync(DateTime desde, DateTime hasta)
        {
            var dias = await _db.Eventos
                .Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= desde && e.Inicio < hasta)
                .Select(e => e.Inicio.Date)
                .Distinct()
                .ToListAsync();
            return dias.ToHashSet();
        }
    }
}
