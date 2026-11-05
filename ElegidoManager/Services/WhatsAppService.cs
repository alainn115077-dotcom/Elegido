using System.Globalization;
using System.Text;
using ElegidoManager.Models;

namespace ElegidoManager.Services
{
    public record PlantillaWhatsApp(string Clave, string Nombre, string Icono, string Mensaje);

    /// <summary>Genera enlaces wa.me con mensajes preparados (sin API de WhatsApp).</summary>
    public static class WhatsApp
    {
        private static readonly CultureInfo Mx = new("es-MX");

        /// <summary>Ladas de Estados Unidos (appsettings: LadasEstadosUnidos). Esos números usan +1 en lugar de +52.</summary>
        public static HashSet<string> LadasEstadosUnidos { get; set; } = new() { "956" };

        public static bool EsDeEstadosUnidos(string? telefono)
        {
            var d = Formato.SoloDigitos(telefono);
            if (d.Length == 11 && d.StartsWith("1")) d = d.Substring(1);
            return d.Length == 10 && LadasEstadosUnidos.Contains(d.Substring(0, 3));
        }

        /// <summary>Deja solo dígitos y agrega el código de país: +1 para ladas de Estados Unidos (956) y +52 para México.</summary>
        public static string NormalizarTelefono(string? telefono)
        {
            if (string.IsNullOrWhiteSpace(telefono)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in telefono) if (char.IsDigit(ch)) sb.Append(ch);
            var digitos = sb.ToString();
            if (digitos.Length == 10)
                digitos = (LadasEstadosUnidos.Contains(digitos.Substring(0, 3)) ? "1" : "52") + digitos;
            return digitos;
        }

        public static string Enlace(string? telefono, string? mensaje = null)
        {
            var numero = NormalizarTelefono(telefono);
            var url = "https://wa.me/" + numero;
            if (!string.IsNullOrWhiteSpace(mensaje)) url += "?text=" + Uri.EscapeDataString(mensaje);
            return url;
        }

        public static List<PlantillaWhatsApp> Plantillas(Contratacion c, GrupoSettings grupo)
        {
            var ev = c.Evento;
            var g = grupo.Nombre;

            // "de hoy", "de mañana" o "del sábado 12 de diciembre"
            var cuando = "la fecha acordada";
            var hora = "";
            if (ev != null)
            {
                var dias = (ev.Inicio.Date - DateTime.Today).Days;
                cuando = dias switch
                {
                    0 => "de hoy",
                    1 => "de mañana",
                    _ => "del " + ev.Inicio.ToString("dddd d 'de' MMMM", Mx)
                };
                if (!ev.HorarioPendiente) hora = " a las " + ev.Inicio.ToString("h:mm tt", Mx);
            }

            return new List<PlantillaWhatsApp>
            {
                new("confirmacion", "Confirmación de contratación", "bi-patch-check",
                    $"Hola, ¿qué tal? Para confirmar su evento {cuando}{hora}. Ahí estaremos, cuente con nosotros."),

                new("horario", "Preguntar horario", "bi-clock",
                    $"Hola, ¿qué tal? Le escribo de {g} para preguntarle a qué hora sería su evento {cuando} " +
                    "y por cuántas horas lo necesita, para dejarlo bien apartado en la agenda. ¡Quedo atento!"),

                new("agradecimiento", "Agradecimiento", "bi-heart",
                    $"Hola, de parte de todo {g} le agradecemos por permitirnos ser parte de su evento. " +
                    "Fue un gusto tocar para ustedes. ¡Esperamos verlos pronto!")
            };
        }
    }
}
