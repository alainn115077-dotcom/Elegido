using System.ComponentModel.DataAnnotations;
using ElegidoManager.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ElegidoManager.ViewModels
{
    public record Vacio(string Icono, string Titulo, string Texto);

    /// <summary>Fila resumida de un evento para tablas y tarjetas.</summary>
    public class EventoResumen
    {
        public int ContratacionId { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string TipoEvento { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public string? Lugar { get; set; }
        public string Direccion { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public EstadoContratacion Estado { get; set; }
        public decimal Precio { get; set; }
        public decimal Pagado { get; set; }
        public decimal Saldo => Precio - Pagado;
        public bool HorarioPendiente { get; set; }

        public string UbicacionReal => string.Join(", ", new[] { Lugar, Direccion, Ciudad }.Where(x => !string.IsNullOrWhiteSpace(x)));
        public bool LugarPendiente => UbicacionReal.Length == 0;
        public string Ubicacion => LugarPendiente ? "Lugar pendiente" : UbicacionReal;
        public string HorarioTexto => Textos.Horario(Inicio, Fin, HorarioPendiente);
    }

    public class DashboardViewModel
    {
        public List<EventoResumen> Proximos { get; set; } = new();
        public bool SinDatos { get; set; }
    }

    public class ContratacionFormViewModel
    {
        public int Id { get; set; }

        // ----- Cliente -----
        [Display(Name = "Cliente")]
        public int? ClienteId { get; set; }

        public bool NuevoCliente { get; set; }

        [StringLength(120)]
        [Display(Name = "Nombre completo")]
        public string? ClienteNombre { get; set; }

        [StringLength(20)]
        [RegularExpression(@"^[0-9 +()\-]{10,20}$", ErrorMessage = "Escribe un teléfono válido (10 dígitos).")]
        [Display(Name = "Teléfono")]
        public string? ClienteTelefono { get; set; }

        [StringLength(80)]
        [Display(Name = "Ciudad del cliente")]
        public string? ClienteCiudad { get; set; }

        // ----- Fecha y horario -----
        [Required(ErrorMessage = "Selecciona el día.")]
        [Range(1, 31, ErrorMessage = "Día no válido.")]
        [Display(Name = "Día")]
        public int? Dia { get; set; }

        [Required(ErrorMessage = "Selecciona el mes.")]
        [Range(1, 12, ErrorMessage = "Mes no válido.")]
        public int? Mes { get; set; }

        /// <summary>Solo en edición: año original del evento (el usuario no lo captura).</summary>
        public int? AnioOriginal { get; set; }
        public int? DiaOriginal { get; set; }
        public int? MesOriginal { get; set; }

        [DataType(DataType.Time)]
        [Display(Name = "Hora de inicio")]
        public TimeSpan? HoraInicio { get; set; }

        [Range(0.5, 12, ErrorMessage = "La duración debe ser de 0.5 a 12 horas.")]
        [Display(Name = "Duración (horas)")]
        public decimal? DuracionHoras { get; set; }

        [StringLength(250)]
        [Display(Name = "Dirección del evento")]
        public string? Direccion { get; set; }

        public bool LugarPendiente { get; set; }
        public bool HorarioPendiente { get; set; }

        /// <summary>Estado actual (solo lectura en el formulario; se cambia desde el detalle).</summary>
        public EstadoContratacion Estado { get; set; } = EstadoContratacion.Pendiente;

        // ----- Dinero -----
        [Required(ErrorMessage = "Escribe el precio acordado.")]
        [Range(1, 9999999, ErrorMessage = "El precio debe ser mayor a cero.")]
        [Display(Name = "Precio acordado")]
        public decimal? PrecioAcordado { get; set; }

        [Range(0, 9999999, ErrorMessage = "Anticipo no válido.")]
        public decimal? Anticipo { get; set; }

        [Display(Name = "Método del anticipo")]
        public MetodoPago MetodoAnticipo { get; set; } = MetodoPago.Efectivo;

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        // ----- Solo lectura en edición -----
        public decimal TotalPagado { get; set; }

        /// <summary>Pagos que no son anticipo (abonos/liquidación) ya registrados.</summary>
        public decimal OtrosPagos { get; set; }

        [ValidateNever] public List<SelectListItem> Clientes { get; set; } = new();

        public bool EsEdicion => Id > 0;
    }

    public class PagoFormViewModel
    {
        public int ContratacionId { get; set; }

        [Required(ErrorMessage = "Escribe el monto.")]
        [Range(0.01, 9999999, ErrorMessage = "El monto debe ser mayor a cero.")]
        public decimal? Monto { get; set; }

        [Required(ErrorMessage = "Selecciona la fecha.")]
        [DataType(DataType.Date)]
        public DateTime? Fecha { get; set; } = DateTime.Today;

        [Display(Name = "Método de pago")]
        public MetodoPago Metodo { get; set; } = MetodoPago.Efectivo;

        [StringLength(100)]
        public string? Referencia { get; set; }

        [StringLength(300)]
        public string? Notas { get; set; }

        [ValidateNever] public string Cliente { get; set; } = string.Empty;
        [ValidateNever] public string TipoEvento { get; set; } = string.Empty;
        public DateTime? FechaEvento { get; set; }
        public decimal Precio { get; set; }
        public decimal Pagado { get; set; }
        public decimal Saldo => Precio - Pagado;
        public bool TienePagos { get; set; }
    }

    public class Movimiento
    {
        public DateTime Fecha { get; set; }
        public bool EsIngreso { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public int? ContratacionId { get; set; }
        public int RegistroId { get; set; }
    }

    public class FinanzasViewModel
    {
        public DateTime Mes { get; set; }

        /// <summary>Suma de tu ganancia en los eventos del mes ya trabajados y con pagos registrados.</summary>
        public decimal IngresosGanados { get; set; }
        public int EventosConGanancia { get; set; }

        /// <summary>Anticipos de los eventos del mes que ya se realizaron.</summary>
        public decimal AnticiposEjecutados { get; set; }
        public int EventosConAnticipo { get; set; }

        public decimal GananciasTotales => IngresosGanados + AnticiposEjecutados;

        public decimal Ingresos { get; set; }
        public decimal Anticipos { get; set; }
        public decimal Gastos { get; set; }
        public decimal Ganancia => Ingresos - Gastos;
        public decimal PorCobrar { get; set; }
        public decimal ValorContratadoMes { get; set; }
        public List<Movimiento> Movimientos { get; set; } = new();
        public List<(string Categoria, decimal Total)> GastosPorCategoria { get; set; } = new();
    }

    public class IngresoEvento
    {
        public int ContratacionId { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string TipoEvento { get; set; } = string.Empty;
        public DateTime? Fecha { get; set; }
        public EstadoContratacion Estado { get; set; }
        public decimal Precio { get; set; }
        public decimal Pagado { get; set; }
        public decimal GastosEvento { get; set; }
        public decimal Saldo => Precio - Pagado;
        public decimal Utilidad => Pagado - GastosEvento;
    }

    public class DocumentoFila
    {
        public int Id { get; set; }
        public string Folio { get; set; } = string.Empty;
        public TipoDocumento Tipo { get; set; }
        public DateTime FechaGeneracion { get; set; }
        public string GeneradoPor { get; set; } = string.Empty;
        public int ContratacionId { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string TipoEvento { get; set; } = string.Empty;
        public DateTime? FechaEvento { get; set; }
        public decimal? MontoPago { get; set; }
    }

    public class ContratacionDetalleViewModel
    {
        public Contratacion Contratacion { get; set; } = null!;
        public List<DocumentoFila> Documentos { get; set; } = new();
        public List<ElegidoManager.Services.PlantillaWhatsApp> Plantillas { get; set; } = new();
        public List<Evento> Conflictos { get; set; } = new();
    }
}

namespace ElegidoManager.ViewModels
{
    /// <summary>Evento ya trabajado, para la lista de Finanzas → Por evento.</summary>
    public class EventoTrabajado
    {
        public int ContratacionId { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public string? Lugar { get; set; }
        public string Direccion { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public decimal Anticipo { get; set; }
        public decimal PagosIntegrantes { get; set; }
        public decimal Gasolina { get; set; }
        public decimal Otros { get; set; }
        public bool Registrado { get; set; }
        public decimal Ganancia => Precio - Anticipo - PagosIntegrantes - Gasolina - Otros;
        public bool HorarioPendiente { get; set; }
        public string HorarioTexto => Textos.Horario(Inicio, Fin, HorarioPendiente);
        public string Ubicacion
        {
            get
            {
                var u = string.Join(", ", new[] { Lugar, Direccion, Ciudad }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return u.Length == 0 ? "Lugar pendiente" : u;
            }
        }
    }

    public class PagoIntegranteFila
    {
        public int IntegranteId { get; set; }
        public string Nombre { get; set; } = string.Empty;

        [Range(0, 9999999, ErrorMessage = "Cantidad no válida.")]
        public decimal? Monto { get; set; }

        /// <summary>Montos rápidos (1 hora / 2 horas) definidos en appsettings: PagosSugeridos.</summary>
        [ValidateNever] public List<decimal> Sugerencias { get; set; } = new();
    }

    /// <summary>Pantalla de pagos de un evento: lo que se le paga a cada integrante, gasolina y la ganancia.</summary>
    public class PagosEventoViewModel
    {
        public int ContratacionId { get; set; }
        public List<PagoIntegranteFila> Integrantes { get; set; } = new();

        [Range(0, 9999999, ErrorMessage = "Cantidad no válida.")]
        public decimal? Gasolina { get; set; }

        [StringLength(150)]
        public string? OtroConcepto { get; set; }

        [Range(0, 9999999, ErrorMessage = "Cantidad no válida.")]
        public decimal? OtroMonto { get; set; }

        /// <summary>Al guardar, registra como pagado el saldo que faltaba del cliente.</summary>
        public bool ClientePagoResto { get; set; } = true;

        // Solo lectura
        [ValidateNever] public string Cliente { get; set; } = string.Empty;
        [ValidateNever] public string Ubicacion { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public decimal Precio { get; set; }
        public decimal Anticipo { get; set; }
        public decimal Pagado { get; set; }
        public decimal Saldo => Precio - Pagado;
    }
}
