using System.Text;
using ElegidoManager.Models;

namespace ElegidoManager.Services
{
    /// <summary>Genera archivos .ics para agregar presentaciones al calendario del celular o de la PC.</summary>
    public static class Calendario
    {
        private static string Esc(string? t) => (t ?? "")
            .Replace("\\", "\\\\").Replace(";", "\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

        private static string Linea(string texto)
        {
            // RFC 5545: líneas de máximo 75 caracteres (se continúan con un espacio)
            var sb = new StringBuilder();
            var resto = texto;
            while (resto.Length > 74)
            {
                sb.Append(resto, 0, 74).Append("\r\n ");
                resto = resto.Substring(74);
            }
            return sb.Append(resto).Append("\r\n").ToString();
        }

        public static string Generar(IEnumerable<Evento> eventos, string nombreGrupo, string? baseUrl)
        {
            var sb = new StringBuilder();
            sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//ELEGIDO MANAGER//ES\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n");
            sb.Append(Linea("X-WR-CALNAME:" + Esc(nombreGrupo)));
            var ahora = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
            foreach (var e in eventos)
            {
                var cliente = e.Contratacion?.Cliente?.NombreCompleto ?? "Presentación";
                sb.Append("BEGIN:VEVENT\r\n");
                sb.Append(Linea($"UID:contratacion-{e.ContratacionId}@elegidomanager"));
                sb.Append($"DTSTAMP:{ahora}\r\n");
                if (e.HorarioPendiente)
                {
                    sb.Append($"DTSTART;VALUE=DATE:{e.Inicio:yyyyMMdd}\r\n");
                    sb.Append($"DTEND;VALUE=DATE:{e.Inicio.AddDays(1):yyyyMMdd}\r\n");
                }
                else
                {
                    // Hora local (la del celular), sin zona: así aparece igual que en la agenda
                    sb.Append($"DTSTART:{e.Inicio:yyyyMMdd'T'HHmmss}\r\n");
                    sb.Append($"DTEND:{e.Fin:yyyyMMdd'T'HHmmss}\r\n");
                }
                sb.Append(Linea("SUMMARY:" + Esc($"{nombreGrupo} · {cliente}")));
                if (!e.LugarPendiente)
                    sb.Append(Linea("LOCATION:" + Esc(e.UbicacionReal)));
                var descripcion = (e.HorarioPendiente ? "Horario por confirmar.\n" : "") +
                                  (e.LugarPendiente ? "Lugar por confirmar.\n" : "Mapa: " + Mapas.Url(e.UbicacionReal) + "\n") +
                                  (string.IsNullOrEmpty(baseUrl) ? "" : "Agenda: " + baseUrl);
                sb.Append(Linea("DESCRIPTION:" + Esc(descripcion.Trim())));
                if (e.Contratacion?.Estado == EstadoContratacion.Cancelada) sb.Append("STATUS:CANCELLED\r\n");
                if (!e.HorarioPendiente)
                    sb.Append("BEGIN:VALARM\r\nACTION:DISPLAY\r\nDESCRIPTION:Presentación en 2 horas\r\nTRIGGER:-PT2H\r\nEND:VALARM\r\n");
                sb.Append("END:VEVENT\r\n");
            }
            sb.Append("END:VCALENDAR\r\n");
            return sb.ToString();
        }
    }
}
