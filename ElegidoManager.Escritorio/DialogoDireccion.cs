namespace ElegidoManager.Escritorio
{
    /// <summary>Pregunta la dirección de la app en la nube (solo la primera vez, o con Shift al abrir).</summary>
    public class DialogoDireccion : Form
    {
        private readonly TextBox _texto;
        public string Direccion { get; private set; } = "";

        public DialogoDireccion(string actual)
        {
            Text = "ELEGIDO MANAGER";
            Icon = Recursos.Icono();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(14, 14, 16);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(520, 200);

            var titulo = new Label
            {
                Text = "Dirección de la aplicación",
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Color.FromArgb(236, 213, 147),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            var ayuda = new Label
            {
                Text = "Escribe la dirección donde está publicada (por ejemplo https://elegido.vercel.app).",
                ForeColor = Color.FromArgb(160, 156, 145),
                Size = new Size(470, 40),
                Location = new Point(24, 54)
            };
            _texto = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(actual) ? "https://" : actual,
                Location = new Point(24, 98),
                Size = new Size(472, 28),
                BackColor = Color.FromArgb(28, 28, 32),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            var aceptar = new Button
            {
                Text = "Abrir",
                Location = new Point(376, 146),
                Size = new Size(120, 36),
                BackColor = Color.FromArgb(201, 165, 76),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            aceptar.FlatAppearance.BorderSize = 0;
            aceptar.Click += (_, _) => Aceptar();
            AcceptButton = aceptar;

            Controls.AddRange(new Control[] { titulo, ayuda, _texto, aceptar });
        }

        private void Aceptar()
        {
            var t = _texto.Text.Trim().TrimEnd('/');
            if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                t = "https://" + t;
            if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            {
                MessageBox.Show(this, "Esa dirección no es válida.", "ELEGIDO MANAGER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Direccion = uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
