using System.Diagnostics;
using System.Net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ElegidoManager.Escritorio
{
    /// <summary>
    /// Ventana de la app de escritorio. Abre ELEGIDO MANAGER desde la nube, así que siempre muestra
    /// la versión más reciente y los mismos datos que la página web y los celulares.
    /// </summary>
    public class VentanaPrincipal : Form
    {
        private readonly Configuracion _config;
        private readonly WebView2 _web;
        private readonly Uri _inicio;

        public VentanaPrincipal(Configuracion config)
        {
            _config = config;
            _inicio = new Uri(config.Url);

            Text = "ELEGIDO MANAGER";
            Icon = Recursos.Icono();
            BackColor = Color.FromArgb(10, 10, 11);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(380, 560);
            Size = new Size(Math.Max(config.Ancho, 800), Math.Max(config.Alto, 600));
            if (config.Maximizada) WindowState = FormWindowState.Maximized;

            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(10, 10, 11) };
            Controls.Add(_web);

            Load += async (_, _) => await IniciarAsync();
            FormClosing += (_, _) => GuardarTamano();
        }

        private async Task IniciarAsync()
        {
            try
            {
                var datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElegidoManager", "WebView2");
                var entorno = await CoreWebView2Environment.CreateAsync(null, datos);
                await _web.EnsureCoreWebView2Async(entorno);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                var r = MessageBox.Show(this,
                    "Falta el componente Microsoft Edge WebView2 (viene en Windows 10/11 actualizados).\n\n¿Abrir la página para instalarlo?",
                    "ELEGIDO MANAGER", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) AbrirFuera("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                Close();
                return;
            }

            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.DocumentTitleChanged += (_, _) =>
                Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "ELEGIDO MANAGER" : core.DocumentTitle;

            // Enlaces externos (WhatsApp, Google Maps, teléfono) se abren en el navegador / app del sistema
            core.NavigationStarting += (_, e) =>
            {
                if (EsDeLaApp(e.Uri) || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || e.Uri == "about:blank") return;
                e.Cancel = true;
                AbrirFuera(e.Uri);
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (EsDeLaApp(e.Uri)) core.Navigate(e.Uri);
                else AbrirFuera(e.Uri);
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    core.NavigateToString(PaginaSinConexion());
            };

            core.Navigate(_inicio.ToString());
        }

        private bool EsDeLaApp(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) &&
            string.Equals(u.Host, _inicio.Host, StringComparison.OrdinalIgnoreCase) &&
            (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

        private static void AbrirFuera(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            if (u.Scheme is not ("https" or "http" or "tel" or "mailto" or "whatsapp")) return;
            try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); }
            catch { /* sin programa asociado */ }
        }

        private string PaginaSinConexion()
        {
            var url = WebUtility.HtmlEncode(_inicio.ToString());
            return $@"<!DOCTYPE html><html lang='es'><head><meta charset='utf-8'><title>Sin conexión · ELEGIDO MANAGER</title>
<style>body{{margin:0;height:100vh;display:grid;place-items:center;background:#0a0a0b;color:#f3efe6;font-family:'Segoe UI',sans-serif}}
.c{{text-align:center;max-width:420px;padding:2rem}}h1{{color:#ecd593;font-size:1.4rem;margin:.5rem 0}}p{{color:#9b978d;line-height:1.5}}
a{{display:inline-block;margin-top:1rem;padding:.75rem 1.6rem;border-radius:12px;background:linear-gradient(145deg,#e6c66f,#b48b2f);color:#0a0a0b;font-weight:700;text-decoration:none}}</style></head>
<body><div class='c'><div style='font-size:2.6rem'>📡</div><h1>No hay conexión con el servidor</h1>
<p>Revisa tu internet. La aplicación se abre desde la nube para tener siempre la información al día.</p>
<a href='{url}'>Reintentar</a></div></body></html>";
        }

        private void GuardarTamano()
        {
            _config.Maximizada = WindowState == FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal)
            {
                _config.Ancho = Width;
                _config.Alto = Height;
            }
            _config.Guardar();
        }
    }
}
