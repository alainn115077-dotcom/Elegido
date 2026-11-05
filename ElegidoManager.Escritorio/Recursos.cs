namespace ElegidoManager.Escritorio
{
    internal static class Recursos
    {
        private static Icon? _icono;

        /// <summary>Ícono del grupo incrustado en el .exe.</summary>
        public static Icon Icono()
        {
            if (_icono != null) return _icono;
            using var s = typeof(Recursos).Assembly.GetManifestResourceStream("ElegidoManager.Escritorio.icono.ico");
            _icono = s != null ? new Icon(s) : SystemIcons.Application;
            return _icono;
        }
    }
}
