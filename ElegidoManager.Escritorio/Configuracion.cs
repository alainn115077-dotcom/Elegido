using System.Text.Json;

namespace ElegidoManager.Escritorio
{
    /// <summary>Ajustes guardados en %AppData%\ElegidoManager\escritorio.json.</summary>
    public class Configuracion
    {
        /// <summary>Dirección de la app en la nube (por ejemplo https://elegido-manager.vercel.app).</summary>
        public string Url { get; set; } = "";
        public int Ancho { get; set; } = 1360;
        public int Alto { get; set; } = 860;
        public bool Maximizada { get; set; }

        private static string Carpeta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElegidoManager");
        private static string Archivo => Path.Combine(Carpeta, "escritorio.json");

        public static Configuracion Cargar()
        {
            try
            {
                if (File.Exists(Archivo))
                    return JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(Archivo)) ?? new Configuracion();

                // Primera vez: si junto al .exe viene un escritorio.json con la dirección, se usa
                var junto = Path.Combine(AppContext.BaseDirectory, "escritorio.json");
                if (File.Exists(junto))
                    return JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(junto)) ?? new Configuracion();
            }
            catch { /* archivo dañado: se vuelve a preguntar */ }
            return new Configuracion();
        }

        public void Guardar()
        {
            try
            {
                Directory.CreateDirectory(Carpeta);
                File.WriteAllText(Archivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* sin permisos: no pasa nada, se pregunta otra vez la próxima */ }
        }
    }
}
