using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ElegidoManager.Models
{
    public class Usuario
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        [Display(Name = "Usuario")]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, StringLength(20)]
        public string Rol { get; set; } = Catalogos.RolAdministrador;

        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public DateTime? UltimoAcceso { get; set; }

        // ---- Seguridad ----
        /// <summary>Intentos fallidos seguidos de contraseña (se reinicia al entrar bien).</summary>
        public int IntentosFallidos { get; set; }
        /// <summary>Si tiene valor y no ha pasado, la cuenta está bloqueada temporalmente.</summary>
        public DateTime? BloqueadoHasta { get; set; }
        /// <summary>Cambia cuando se cambia la contraseña, el rol o se desactiva: cierra las sesiones abiertas.</summary>
        [StringLength(64)]
        public string? SelloSeguridad { get; set; }

        /// <summary>Para cuentas con rol Integrante: a qué integrante del grupo corresponde (para ver sus deudas).</summary>
        public int? IntegranteId { get; set; }
        public Integrante? Integrante { get; set; }
    }

    public class Cliente
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(120)]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "El teléfono es obligatorio."), StringLength(20)]
        [RegularExpression(@"^[0-9 +()\-]{10,20}$", ErrorMessage = "Escribe un teléfono válido (10 dígitos).")]
        [Display(Name = "Teléfono")]
        public string Telefono { get; set; } = string.Empty;

        [StringLength(80)]
        public string? Ciudad { get; set; }

        [StringLength(120), EmailAddress(ErrorMessage = "Correo no válido.")]
        public string? Correo { get; set; }

        [StringLength(500)]
        public string? Notas { get; set; }

        public DateTime FechaRegistro { get; set; } = DateTime.Now;

        [ValidateNever]
        public ICollection<Contratacion> Contrataciones { get; set; } = new List<Contratacion>();
    }

    public class Servicio
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(300)]
        [Display(Name = "Descripción")]
        public string? Descripcion { get; set; }

        [Range(0.5, 24, ErrorMessage = "Entre 0.5 y 24 horas.")]
        [Display(Name = "Duración (horas)")]
        public decimal? DuracionHoras { get; set; }

        [Range(0, 9999999, ErrorMessage = "Precio no válido.")]
        [Display(Name = "Precio base")]
        public decimal? PrecioBase { get; set; }

        public bool Activo { get; set; } = true;

        [ValidateNever]
        public ICollection<Contratacion> Contrataciones { get; set; } = new List<Contratacion>();
    }

    public class Contratacion
    {
        public int Id { get; set; }

        public int ClienteId { get; set; }
        public Cliente Cliente { get; set; } = null!;

        public int? ServicioId { get; set; }
        public Servicio? Servicio { get; set; }

        [Required, StringLength(50)]
        public string TipoEvento { get; set; } = string.Empty;

        public EstadoContratacion Estado { get; set; } = EstadoContratacion.Pendiente;

        public decimal PrecioAcordado { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [StringLength(300)]
        public string? MotivoCancelacion { get; set; }

        public DateTime FechaRegistro { get; set; } = DateTime.Now;
        public DateTime? FechaActualizacion { get; set; }

        public Evento? Evento { get; set; }
        public ICollection<Pago> Pagos { get; set; } = new List<Pago>();
        public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
        public ICollection<Gasto> Gastos { get; set; } = new List<Gasto>();

        /// <summary>Suma de pagos cargados (requiere Include de Pagos).</summary>
        [NotMapped]
        public decimal TotalPagado => Pagos.Sum(p => p.Monto);

        /// <summary>Saldo pendiente calculado: precio acordado menos pagos registrados.</summary>
        [NotMapped]
        public decimal Saldo => PrecioAcordado - TotalPagado;
    }

    /// <summary>Datos de agenda y logística de una contratación (relación 1 a 1).</summary>
    public class Evento
    {
        public int Id { get; set; }

        public int ContratacionId { get; set; }
        public Contratacion Contratacion { get; set; } = null!;

        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }

        [StringLength(120)]
        public string? Lugar { get; set; }

        [Required, StringLength(250)]
        public string Direccion { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string Ciudad { get; set; } = string.Empty;

        [StringLength(150)]
        public string? PuntoReunion { get; set; }

        public TimeSpan? HoraReunion { get; set; }
        public TimeSpan? HoraSalida { get; set; }

        [StringLength(500)]
        public string? NotasLogistica { get; set; }

        /// <summary>El cliente todavía no confirma la hora: el evento ocupa el día sin horario fijo.</summary>
        public bool HorarioPendiente { get; set; }

        [NotMapped]
        public TimeSpan Duracion => Fin - Inicio;

        /// <summary>Lugar, dirección y ciudad que existan, separados por coma (vacío si el lugar está pendiente).</summary>
        [NotMapped]
        public string UbicacionReal => string.Join(", ", new[] { Lugar, Direccion, Ciudad }.Where(x => !string.IsNullOrWhiteSpace(x)));

        [NotMapped]
        public bool LugarPendiente => UbicacionReal.Length == 0;

        [NotMapped]
        public string Ubicacion => LugarPendiente ? "Lugar pendiente" : UbicacionReal;

        [NotMapped]
        public string HorarioTexto => Textos.Horario(Inicio, Fin, HorarioPendiente);
    }

    public class Pago
    {
        public int Id { get; set; }

        public int ContratacionId { get; set; }
        public Contratacion Contratacion { get; set; } = null!;

        public TipoPago Tipo { get; set; }
        public MetodoPago Metodo { get; set; }
        public decimal Monto { get; set; }
        public DateTime Fecha { get; set; }

        [StringLength(100)]
        public string? Referencia { get; set; }

        [StringLength(300)]
        public string? Notas { get; set; }

        public DateTime FechaRegistro { get; set; } = DateTime.Now;
    }

    public class Gasto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El concepto es obligatorio."), StringLength(150)]
        public string Concepto { get; set; } = string.Empty;

        [Required(ErrorMessage = "Selecciona una categoría."), StringLength(50)]
        [Display(Name = "Categoría")]
        public string Categoria { get; set; } = string.Empty;

        [Range(0.01, 9999999, ErrorMessage = "El monto debe ser mayor a cero.")]
        public decimal Monto { get; set; }

        [DataType(DataType.Date)]
        public DateTime Fecha { get; set; } = DateTime.Today;

        [Display(Name = "Evento relacionado")]
        public int? ContratacionId { get; set; }

        [ValidateNever]
        public Contratacion? Contratacion { get; set; }

        /// <summary>Si el gasto es un pago a un integrante, a quién se le pagó.</summary>
        public int? IntegranteId { get; set; }

        [ValidateNever]
        public Integrante? Integrante { get; set; }

        [StringLength(300)]
        public string? Notas { get; set; }
    }

    public class Integrante
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(120)]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "Indica el instrumento o rol."), StringLength(80)]
        [Display(Name = "Instrumento / rol")]
        public string Instrumento { get; set; } = string.Empty;

        [StringLength(20)]
        [RegularExpression(@"^[0-9 +()\-]{10,20}$", ErrorMessage = "Escribe un teléfono válido (10 dígitos).")]
        [Display(Name = "Teléfono")]
        public string? Telefono { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Fecha de ingreso")]
        public DateTime? FechaIngreso { get; set; }

        public bool Activo { get; set; } = true;

        [StringLength(300)]
        public string? Notas { get; set; }

        [ValidateNever]
        public ICollection<MovimientoIntegrante> Movimientos { get; set; } = new List<MovimientoIntegrante>();
    }

    /// <summary>Dinero pendiente entre el representante y un integrante (en cualquiera de los dos sentidos).</summary>
    public class MovimientoIntegrante
    {
        public int Id { get; set; }

        public int IntegranteId { get; set; }
        public Integrante Integrante { get; set; } = null!;

        public DireccionDeuda Direccion { get; set; }
        public decimal Monto { get; set; }

        [StringLength(150)]
        public string? Concepto { get; set; }

        public DateTime Fecha { get; set; } = DateTime.Now;
        public bool Liquidado { get; set; }
        public DateTime? FechaLiquidado { get; set; }
        public DateTime FechaRegistro { get; set; } = DateTime.Now;
    }

    public class Reunion
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Escribe un título."), StringLength(120)]
        [Display(Name = "Título")]
        public string Titulo { get; set; } = string.Empty;

        [Required, StringLength(30)]
        public string Tipo { get; set; } = "Ensayo";

        [Display(Name = "Fecha y hora")]
        public DateTime FechaHora { get; set; } = DateTime.Today.AddHours(18);

        [StringLength(150)]
        public string? Lugar { get; set; }

        [StringLength(500)]
        public string? Notas { get; set; }
    }

    public class Cancion
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El título es obligatorio."), StringLength(150)]
        [Display(Name = "Título")]
        public string Titulo { get; set; } = string.Empty;

        [StringLength(120)]
        [Display(Name = "Artista / autor")]
        public string? Artista { get; set; }

        [StringLength(60)]
        [Display(Name = "Género")]
        public string? Genero { get; set; }

        [StringLength(20)]
        public string? Tonalidad { get; set; }

        [StringLength(300)]
        public string? Notas { get; set; }

        [Display(Name = "En repertorio activo")]
        public bool Activa { get; set; } = true;
    }

    public class Documento
    {
        public int Id { get; set; }

        public int ContratacionId { get; set; }
        public Contratacion Contratacion { get; set; } = null!;

        public int? PagoId { get; set; }
        public Pago? Pago { get; set; }

        public TipoDocumento Tipo { get; set; }

        [Required, StringLength(30)]
        public string Folio { get; set; } = string.Empty;

        public DateTime FechaGeneracion { get; set; } = DateTime.Now;

        [Required, StringLength(100)]
        public string GeneradoPor { get; set; } = string.Empty;

        [Required, StringLength(150)]
        public string NombreArchivo { get; set; } = string.Empty;

        public byte[] Contenido { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Bitácora de seguridad: quién entró, cuándo y qué cambió.</summary>
    public class Bitacora
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string Usuario { get; set; } = string.Empty;

        [StringLength(60)]
        public string Accion { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Detalle { get; set; }

        [StringLength(60)]
        public string? Ip { get; set; }
    }
}
