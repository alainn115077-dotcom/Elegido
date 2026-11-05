namespace ElegidoManager.Models
{
    public enum EstadoContratacion { Pendiente, Confirmada, Cancelada }

    public enum TipoPago { Anticipo, Abono, Liquidacion }

    public enum MetodoPago { Efectivo, Transferencia, Deposito, Otro }

    public enum TipoDocumento { Contrato, ComprobantePago, ReciboLiquidacion }

    /// <summary>MeDebe: el integrante me debe a mí. LeDebo: yo le debo al integrante.</summary>
    public enum DireccionDeuda { MeDebe, LeDebo }

    public static class Catalogos
    {
        public static readonly string[] TiposEvento =
        {
            "Boda", "XV Años", "Cumpleaños", "Bautizo", "Primera Comunión", "Graduación",
            "Aniversario", "Evento privado", "Evento público", "Baile", "Serenata", "Otro"
        };

        public static readonly string[] CategoriasGasto =
        {
            "Combustible", "Transporte", "Comida", "Hospedaje", "Pago a integrantes",
            "Equipo y mantenimiento", "Publicidad", "Vestuario", "Otro"
        };

        public const string CategoriaPagoIntegrantes = "Pago a integrantes";
        public const string CategoriaGasolina = "Combustible";
        public const string CategoriaOtro = "Otro";
        /// <summary>Marca en Notas de los gastos capturados en Finanzas → Pagos del evento.</summary>
        public const string MarcaPagosEvento = "Pagos del evento";

        public static readonly string[] TiposReunion = { "Ensayo", "Junta", "Grabación", "Sesión de fotos", "Otro" };

        public const string RolAdministrador = "Administrador";
        public const string RolColaborador = "Colaborador";
        /// <summary>Integrante del grupo: solo ve Inicio y Agenda (fechas, horarios y lugares), sin pagos.</summary>
        public const string RolIntegrante = "Integrante";
        /// <summary>Roles con acceso al negocio (todo excepto lo exclusivo del administrador).</summary>
        public const string RolesEquipo = RolAdministrador + "," + RolColaborador;
        /// <summary>Roles que tienen el apartado "Deudas" (sus cuentas con el administrador).</summary>
        public const string RolesConDeudas = RolColaborador + "," + RolIntegrante;
        public static bool LigaIntegrante(string? rol) => rol == RolColaborador || rol == RolIntegrante;
        public static readonly string[] Roles = { RolAdministrador, RolColaborador, RolIntegrante };
    }

    public static class Textos
    {
        private static readonly System.Globalization.CultureInfo Mx = new("es-MX");

        /// <summary>"7:00 p. m. – 9:00 p. m." o "Horario pendiente".</summary>
        public static string Horario(DateTime inicio, DateTime fin, bool pendiente) =>
            pendiente ? "Horario pendiente" : $"{inicio.ToString("h:mm tt", Mx)} – {fin.ToString("h:mm tt", Mx)}";

        public static string Nombre(this EstadoContratacion e) => e switch
        {
            EstadoContratacion.Confirmada => "Confirmada",
            EstadoContratacion.Cancelada => "Cancelada",
            _ => "Pendiente"
        };

        /// <summary>Clase CSS del indicador de estado (verde / amarillo / rojo).</summary>
        public static string Clase(this EstadoContratacion e) => e switch
        {
            EstadoContratacion.Confirmada => "estado-confirmada",
            EstadoContratacion.Cancelada => "estado-cancelada",
            _ => "estado-pendiente"
        };

        public static string Nombre(this TipoPago t) => t switch
        {
            TipoPago.Anticipo => "Anticipo",
            TipoPago.Liquidacion => "Liquidación",
            _ => "Abono"
        };

        public static string Nombre(this MetodoPago m) => m switch
        {
            MetodoPago.Transferencia => "Transferencia",
            MetodoPago.Deposito => "Depósito",
            MetodoPago.Otro => "Otro",
            _ => "Efectivo"
        };

        public static string Nombre(this TipoDocumento t) => t switch
        {
            TipoDocumento.Contrato => "Contrato de servicios",
            TipoDocumento.ComprobantePago => "Comprobante de pago",
            _ => "Recibo de liquidación"
        };

        public static string Prefijo(this TipoDocumento t) => t switch
        {
            TipoDocumento.Contrato => "CTR",
            TipoDocumento.ComprobantePago => "CMP",
            _ => "LIQ"
        };
    }
}
