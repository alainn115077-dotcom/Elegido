namespace ElegidoManager.Escritorio
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            var config = Configuracion.Cargar();
            var reconfigurar = args.Contains("--configurar") || (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            if (reconfigurar || string.IsNullOrWhiteSpace(config.Url))
            {
                using var dlg = new DialogoDireccion(config.Url);
                if (dlg.ShowDialog() != DialogResult.OK) return;
                config.Url = dlg.Direccion;
                config.Guardar();
            }

            Application.Run(new VentanaPrincipal(config));
        }
    }
}
