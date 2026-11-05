using System.Globalization;

namespace ElegidoManager.Services
{
    /// <summary>
    /// Enlaces a Google Maps. Si la dirección no menciona ninguna ciudad conocida,
    /// se le agrega la ciudad base (Reynosa) para que la búsqueda encuentre el lugar más parecido de la zona.
    /// </summary>
    public static class Mapas
    {
        public const string CiudadBase = "Reynosa, Tamaulipas";

        private static readonly string[] CiudadesConocidas =
        {
            "reynosa", "rio bravo", "matamoros", "valle hermoso", "diaz ordaz", "camargo", "miguel aleman",
            "nuevo laredo", "monterrey", "san fernando", "mier", "guerrero", "mcallen", "hidalgo", "pharr",
            "edinburg", "mission", "brownsville", "tamaulipas", "nuevo leon", "texas"
        };

        private static readonly System.Text.RegularExpressions.Regex Decimales =
            new(@"^\s*-?\d{1,3}(\.\d+)?\s*[, ]\s*-?\d{1,3}(\.\d+)?\s*$");
        private static readonly System.Text.RegularExpressions.Regex Grados =
            new(@"\d+\s*°.*[NSns].*\d+\s*°.*[EWOeow]");

        /// <summary>"26.0508, -98.2979" o "26°03'03.0\"N 98°17'52.0\"W": coordenadas que se buscan tal cual.</summary>
        public static bool EsCoordenada(string? texto)
        {
            var t = (texto ?? string.Empty).Trim();
            return t.Length > 0 && (Decimales.IsMatch(t) || Grados.IsMatch(t));
        }

        public static string Consulta(string? direccion)
        {
            var d = (direccion ?? string.Empty).Trim();
            if (d.Length == 0) return CiudadBase;
            if (EsCoordenada(d)) return d;
            var comparador = CultureInfo.InvariantCulture.CompareInfo;
            var opciones = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            var tieneCiudad = CiudadesConocidas.Any(c => comparador.IndexOf(d, c, opciones) >= 0);
            return tieneCiudad ? d : $"{d}, {CiudadBase}";
        }

        public static string Url(string? direccion) =>
            "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(Consulta(direccion));
    }
}
