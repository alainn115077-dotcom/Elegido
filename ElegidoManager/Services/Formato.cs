using System.Text;
using System.Text.RegularExpressions;

namespace ElegidoManager.Services
{
    /// <summary>Da formato uniforme a teléfonos y direcciones capturadas.</summary>
    public static class Formato
    {
        /// <summary>"8994947922" → "899 494 7922". Si no tiene 10 dígitos, lo deja como se escribió.</summary>
        public static string Telefono(string? telefono)
        {
            var t = (telefono ?? string.Empty).Trim();
            var digitos = new string(t.Where(char.IsDigit).ToArray());
            if (digitos.Length == 12 && digitos.StartsWith("52")) digitos = digitos.Substring(2);
            if (digitos.Length == 11 && digitos.StartsWith("1")) digitos = digitos.Substring(1); // +1 de Estados Unidos
            return digitos.Length == 10 ? $"{digitos[..3]} {digitos.Substring(3, 3)} {digitos.Substring(6)}" : t;
        }

        /// <summary>Solo los dígitos del teléfono (para comparar sin importar los espacios).</summary>
        public static string SoloDigitos(string? telefono) => new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());

        private static readonly HashSet<string> Particulas = new() { "de", "del", "y", "e", "en", "a" };
        private static readonly HashSet<string> SinGato = new() { "km", "km.", "cp", "c.p.", "cp.", "no", "no.", "num", "num.", "núm", "núm.", "#", "n°", "lote", "mz", "mz.", "manzana", "piso", "int", "int.", "interior" };
        private static readonly Regex Numero = new(@"^\d{1,5}(-?[A-Za-z])?$", RegexOptions.Compiled);

        /// <summary>
        /// "PALAPA sm calle actopan 338" → "Palapa SM Calle Actopan #338".
        /// Pone cada palabra con mayúscula inicial (aunque se haya escrito toda en mayúsculas o minúsculas),
        /// deja en mayúsculas las siglas sin vocales y agrega # antes del número de la casa.
        /// </summary>
        public static string Direccion(string? direccion)
        {
            var texto = Regex.Replace((direccion ?? string.Empty).Trim(), @"\s+", " ");
            if (texto.Length == 0 || Mapas.EsCoordenada(texto)) return texto; // las coordenadas se dejan tal cual

            var palabras = texto.Split(' ');
            var sb = new StringBuilder();
            for (var i = 0; i < palabras.Length; i++)
            {
                var p = palabras[i];
                var anterior = i > 0 ? palabras[i - 1].ToLowerInvariant().TrimEnd(',') : "";

                var sinComa = p.TrimEnd(',');
                if (Numero.IsMatch(sinComa) && i > 0 && !SinGato.Contains(anterior) && !anterior.StartsWith("#"))
                    p = "#" + p.ToUpperInvariant();
                else if (p.Length > 0 && char.IsLetter(p[0]))
                {
                    var baja = p.ToLowerInvariant();
                    var limpia = baja.TrimEnd(',', '.');
                    if (limpia == "km" || limpia == "kms" || (i > 0 && Particulas.Contains(limpia)))
                        p = baja;
                    else if (limpia.Length <= 4 && limpia.All(char.IsLetter) && !limpia.Any(c => "aeiouáéíóúü".Contains(c)))
                        p = p.ToUpperInvariant(); // siglas: SM, CTM...
                    else
                        p = char.ToUpperInvariant(baja[0]) + baja.Substring(1);
                }

                if (i > 0) sb.Append(' ');
                sb.Append(p);
            }
            return sb.ToString();
        }
    }
}
