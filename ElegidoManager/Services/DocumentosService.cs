using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ElegidoManager.Services
{
    /// <summary>Genera los PDF (QuestPDF) y los guarda en la tabla Documentos.</summary>
    public class DocumentosService
    {
        private static readonly CultureInfo Mx = new("es-MX");

        private const string Negro = "#0B0B0C";
        private const string Oro = "#C9A54C";
        private const string OroOscuro = "#8A6D24";
        private const string Crema = "#FBF7EC";
        private const string OroClaro = "#E6D3A1";
        private const string Texto = "#1C1C1E";
        private const string GrisTexto = "#6B6B70";
        private const string GrisLinea = "#E2E2E5";

        private readonly AppDbContext _db;
        private readonly GrupoSettings _grupo;
        private readonly byte[]? _logo;
        private readonly byte[]? _firma;

        public DocumentosService(AppDbContext db, IOptions<GrupoSettings> grupo, IWebHostEnvironment env)
        {
            _db = db;
            _grupo = grupo.Value;
            _logo = LeerImagen(Path.Combine(env.WebRootPath, "img", "logo-grupo.png"));
            _firma = LeerImagen(Path.Combine(env.WebRootPath, "img", "firma.png"));
        }

        private static byte[]? LeerImagen(string ruta) => File.Exists(ruta) ? File.ReadAllBytes(ruta) : null;

        /// <summary>Nombre de quien firma por el grupo (appsettings: Grupo:Representante).</summary>
        private string Representante => string.IsNullOrWhiteSpace(_grupo.Representante) ? _grupo.Nombre : _grupo.Representante;

        public async Task<Documento> GenerarAsync(int contratacionId, TipoDocumento tipo, int? pagoId, string usuario)
        {
            var c = await _db.Contrataciones
                .Include(x => x.Cliente)
                .Include(x => x.Evento)
                .Include(x => x.Pagos)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == contratacionId)
                ?? throw new InvalidOperationException("La contratación no existe.");

            if (c.Evento == null) throw new InvalidOperationException("La contratación no tiene fecha registrada.");

            Pago? pago = null;
            switch (tipo)
            {
                case TipoDocumento.Contrato:
                    if (c.Estado == EstadoContratacion.Cancelada)
                        throw new InvalidOperationException("No se puede generar contrato de una contratación cancelada.");
                    break;
                case TipoDocumento.ComprobantePago:
                    pago = c.Pagos.FirstOrDefault(p => p.Id == pagoId)
                           ?? throw new InvalidOperationException("Selecciona un pago válido para generar el comprobante.");
                    break;
                case TipoDocumento.ReciboLiquidacion:
                    if (c.Saldo > 0 || c.Pagos.Count == 0)
                        throw new InvalidOperationException($"El recibo de liquidación solo se genera cuando el saldo es $0. Saldo actual: {c.Saldo.ToString("C", Mx)}.");
                    break;
            }

            var folio = await SiguienteFolioAsync(tipo);
            var bytes = tipo switch
            {
                TipoDocumento.Contrato => Contrato(c, folio),
                TipoDocumento.ComprobantePago => Comprobante(c, pago!, folio),
                _ => ReciboLiquidacion(c, folio)
            };

            var doc = new Documento
            {
                ContratacionId = c.Id,
                PagoId = pago?.Id,
                Tipo = tipo,
                Folio = folio,
                FechaGeneracion = DateTime.Now,
                GeneradoPor = usuario,
                NombreArchivo = $"{folio}_{Limpiar(c.Cliente.NombreCompleto)}.pdf",
                Contenido = bytes
            };
            _db.Documentos.Add(doc);
            await _db.SaveChangesAsync();
            return doc;
        }

        private async Task<string> SiguienteFolioAsync(TipoDocumento tipo)
        {
            var anio = DateTime.Now.Year;
            var prefijo = $"{tipo.Prefijo()}-{anio}-";
            var existentes = await _db.Documentos.Where(d => d.Folio.StartsWith(prefijo)).Select(d => d.Folio).ToListAsync();
            var max = 0;
            foreach (var f in existentes)
                if (int.TryParse(f.Substring(prefijo.Length), out var n) && n > max) max = n;
            return $"{prefijo}{(max + 1):0000}";
        }

        private static string Limpiar(string s)
        {
            var chars = s.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray();
            return new string(chars).Trim().Replace(' ', '_');
        }

        // =====================================================================
        //  DOCUMENTOS
        // =====================================================================
        private byte[] Contrato(Contratacion c, string folio)
        {
            var ev = c.Evento!;
            var anticipo = c.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto);
            var grupo = _grupo.Nombre;

            var clausulas = new[]
            {
                $"OBJETO. {grupo} (en adelante \"EL GRUPO\") se compromete a realizar una presentación musical en vivo para el evento de {c.Cliente.NombreCompleto} (en adelante \"EL CLIENTE\"), en la fecha, horario y lugar indicados en este documento.",
                ev.HorarioPendiente
                    ? "HORARIO. El horario de la presentación se confirmará entre ambas partes antes del evento. El tiempo adicional que EL CLIENTE solicite el día del evento se cotizará y pagará por separado."
                    : $"HORARIO. La presentación iniciará a las {ev.Inicio.ToString("h:mm tt", Mx)} y concluirá a las {ev.Fin.ToString("h:mm tt", Mx)}, con una duración total de {Duracion(ev)}. El tiempo adicional que EL CLIENTE solicite el día del evento se cotizará y pagará por separado.",
                $"PRECIO. El precio total acordado por el servicio es de {c.PrecioAcordado.ToString("C", Mx)} ({Letras(c.PrecioAcordado)}).",
                anticipo > 0
                    ? $"ANTICIPO. EL CLIENTE entrega un anticipo de {anticipo.ToString("C", Mx)} para apartar la fecha. El saldo restante de {c.Saldo.ToString("C", Mx)} deberá liquidarse antes de iniciar la presentación."
                    : "ANTICIPO. Para apartar la fecha, EL CLIENTE deberá entregar el anticipo acordado. El saldo restante deberá liquidarse antes de iniciar la presentación.",
                "CANCELACIÓN. Si EL CLIENTE cancela el evento, el anticipo no será reembolsable, ya que la fecha fue reservada en exclusiva. Un cambio de fecha queda sujeto a la disponibilidad de la agenda de EL GRUPO.",
                "CONDICIONES DEL LUGAR. EL CLIENTE proporcionará un espacio techado o protegido para los músicos y el equipo, con conexión eléctrica cercana y en buen estado. EL GRUPO no se hace responsable por interrupciones causadas por fallas eléctricas, clima o causas ajenas a su control.",
                "SEGURIDAD. EL CLIENTE se compromete a garantizar un ambiente seguro para los integrantes de EL GRUPO y su equipo durante el evento. EL GRUPO podrá suspender la presentación si existe riesgo para su integridad, sin obligación de reembolso.",
                "ACEPTACIÓN. Ambas partes leen y aceptan las condiciones de este contrato, firmando de conformidad."
            };

            return Construir("Contrato de servicios", folio, col =>
            {
                col.Item().Text(t =>
                {
                    t.Span("Contrato de prestación de servicios musicales que celebran ").FontColor(GrisTexto);
                    t.Span(grupo).SemiBold();
                    t.Span(" y ").FontColor(GrisTexto);
                    t.Span(c.Cliente.NombreCompleto).SemiBold();
                    t.Span(", conforme a lo siguiente:").FontColor(GrisTexto);
                });

                Titulo(col, "Datos del cliente");
                col.Item().Element(x => TablaDatos(x, new (string, string)[]
                {
                    ("Nombre", c.Cliente.NombreCompleto),
                    ("Teléfono", c.Cliente.Telefono)
                }));

                Titulo(col, "Datos del evento");
                col.Item().Element(x => TablaDatos(x, new (string, string)[]
                {
                    ("Fecha", Capital(ev.Inicio.ToString("dddd d 'de' MMMM 'de' yyyy", Mx))),
                    ("Horario", ev.HorarioPendiente ? "Por confirmar" : $"{ev.Inicio.ToString("h:mm tt", Mx)} a {ev.Fin.ToString("h:mm tt", Mx)} ({Duracion(ev)})"),
                    ("Dirección", ev.LugarPendiente ? "Por confirmar" : ev.UbicacionReal)
                }));

                CajaMontos(col, c.PrecioAcordado, c.TotalPagado, c.Saldo);

                Titulo(col, "Cláusulas");
                for (var i = 0; i < clausulas.Length; i++)
                {
                    var n = i + 1;
                    var texto = clausulas[i];
                    var punto = texto.IndexOf('.');
                    col.Item().PaddingBottom(6).Text(t =>
                    {
                        t.Span($"{n}. {texto.Substring(0, punto + 1)} ").Bold();
                        t.Span(texto.Substring(punto + 1).Trim()).LineHeight(1.35f);
                    });
                }

                if (!string.IsNullOrWhiteSpace(c.Observaciones))
                {
                    Titulo(col, "Observaciones");
                    col.Item().Text(c.Observaciones).LineHeight(1.35f);
                }

                Firmas(col, (c.Cliente.NombreCompleto, "EL CLIENTE", false), (Representante, "POR EL GRUPO", true));
            });
        }

        private byte[] Comprobante(Contratacion c, Pago pago, string folio)
        {
            var ev = c.Evento!;
            var titulo = pago.Tipo switch
            {
                TipoPago.Anticipo => "Comprobante de anticipo",
                TipoPago.Liquidacion => "Comprobante de liquidación",
                _ => "Comprobante de abono"
            };
            var pagadoHastaAqui = c.Pagos.Where(p => p.Fecha < pago.Fecha || (p.Fecha == pago.Fecha && p.Id <= pago.Id)).Sum(p => p.Monto);
            var saldoDespues = c.PrecioAcordado - pagadoHastaAqui;

            return Construir(titulo, folio, col =>
            {
                col.Item().PaddingTop(6).Background(Crema).Border(0.5f).BorderColor(OroClaro).Padding(16).Column(x =>
                {
                    x.Item().Text("CANTIDAD RECIBIDA").FontSize(8).FontColor(GrisTexto);
                    x.Item().Text(pago.Monto.ToString("C", Mx)).FontSize(26).Bold().FontColor(OroOscuro);
                    x.Item().Text(Letras(pago.Monto)).FontSize(9).FontColor(Texto);
                });

                col.Item().PaddingTop(14).Text(t =>
                {
                    t.Span("Recibimos de ");
                    t.Span(c.Cliente.NombreCompleto).SemiBold();
                    t.Span($" la cantidad arriba indicada por concepto de {pago.Tipo.Nombre().ToLower(Mx)} del servicio musical para su evento del ");
                    t.Span(ev.Inicio.ToString("dddd d 'de' MMMM 'de' yyyy", Mx)).SemiBold();
                    t.Span(".");
                });

                Titulo(col, "Detalle del pago");
                col.Item().Element(x => TablaDatos(x, new (string, string)[]
                {
                    ("Fecha del pago", pago.Fecha.ToString("d 'de' MMMM 'de' yyyy", Mx)),
                    ("Tipo", pago.Tipo.Nombre()),
                    ("Método de pago", pago.Metodo.Nombre()),
                    ("Referencia", string.IsNullOrEmpty(pago.Referencia) ? "—" : pago.Referencia),
                    ("Evento", ev.HorarioPendiente ? ev.Inicio.ToString("d MMM yyyy", Mx) : ev.Inicio.ToString("d MMM yyyy, h:mm tt", Mx)),
                    ("Dirección", ev.LugarPendiente ? "Por confirmar" : ev.UbicacionReal)
                }));

                CajaMontos(col, c.PrecioAcordado, pagadoHastaAqui, saldoDespues);

                Firmas(col, (Representante, "RECIBIÓ POR EL GRUPO", true), null);
            });
        }

        private byte[] ReciboLiquidacion(Contratacion c, string folio)
        {
            var ev = c.Evento!;
            return Construir("Recibo de liquidación", folio, col =>
            {
                col.Item().Text(t =>
                {
                    t.Span("Por medio del presente, ");
                    t.Span(_grupo.Nombre).SemiBold();
                    t.Span(" hace constar que ");
                    t.Span(c.Cliente.NombreCompleto).SemiBold();
                    t.Span($" ha cubierto en su totalidad el importe de {c.PrecioAcordado.ToString("C", Mx)} ({Letras(c.PrecioAcordado)}) correspondiente al servicio musical de su evento del ");
                    t.Span(ev.Inicio.ToString("dddd d 'de' MMMM 'de' yyyy", Mx)).SemiBold();
                    t.Span(", por lo que la cuenta queda ");
                    t.Span("TOTALMENTE LIQUIDADA").Bold().FontColor(OroOscuro);
                    t.Span(".");
                });

                Titulo(col, "Pagos recibidos");
                col.Item().Table(tabla =>
                {
                    tabla.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                    });
                    tabla.Header(h =>
                    {
                        foreach (var titulo in new[] { "Fecha", "Tipo", "Método", "Monto" })
                            h.Cell().Background(Negro).Padding(6).Text(titulo).FontSize(8).Bold().FontColor(Oro);
                    });
                    foreach (var p in c.Pagos.OrderBy(p => p.Fecha))
                    {
                        tabla.Cell().BorderBottom(0.5f).BorderColor(GrisLinea).Padding(6).Text(p.Fecha.ToString("d MMM yyyy", Mx));
                        tabla.Cell().BorderBottom(0.5f).BorderColor(GrisLinea).Padding(6).Text(p.Tipo.Nombre());
                        tabla.Cell().BorderBottom(0.5f).BorderColor(GrisLinea).Padding(6).Text(p.Metodo.Nombre());
                        tabla.Cell().BorderBottom(0.5f).BorderColor(GrisLinea).Padding(6).AlignRight().Text(p.Monto.ToString("C", Mx)).SemiBold();
                    }
                });

                CajaMontos(col, c.PrecioAcordado, c.TotalPagado, c.Saldo);

                col.Item().PaddingTop(12).Text("¡Gracias por confiar en nosotros para su evento!").Italic().FontColor(GrisTexto);

                Firmas(col, (Representante, "POR EL GRUPO", true), (c.Cliente.NombreCompleto, "EL CLIENTE", false));
            });
        }

        // =====================================================================
        //  COMPONENTES DE DISEÑO
        // =====================================================================
        private byte[] Construir(string titulo, string folio, Action<ColumnDescriptor> contenido)
        {
            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(36);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Texto));
                    page.Header().Element(h => Encabezado(h, titulo, folio));
                    page.Content().PaddingVertical(18).Column(contenido);
                    page.Footer().Element(Pie);
                });
            }).GeneratePdf();
        }

        private void Encabezado(IContainer contenedor, string titulo, string folio)
        {
            contenedor.Column(col =>
            {
                col.Item().Background(Negro).PaddingVertical(12).PaddingHorizontal(18).Row(row =>
                {
                    if (_logo != null)
                    {
                        row.ConstantItem(150).Height(62).AlignMiddle().Image(_logo).FitArea();
                        row.ConstantItem(14);
                    }
                    row.RelativeItem().AlignMiddle().Column(x =>
                    {
                        x.Item().Text(_grupo.Nombre.ToUpper(Mx)).FontSize(_logo != null ? 13 : 17).Bold().FontColor(Oro);
                        var contacto = string.Join("  ·  ", new[]
                        {
                            _grupo.Ciudad,
                            string.IsNullOrWhiteSpace(_grupo.Telefono) ? null : "Tel. " + _grupo.Telefono,
                            _grupo.Correo
                        }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (!string.IsNullOrEmpty(contacto))
                            x.Item().PaddingTop(3).Text(contacto).FontSize(8).FontColor("#BDBDBD");
                    });
                    row.ConstantItem(170).AlignMiddle().Column(x =>
                    {
                        x.Item().AlignRight().Text(titulo.ToUpper(Mx)).FontSize(10).Bold().FontColor(Colors.White);
                        x.Item().AlignRight().Text("Folio " + folio).FontSize(9).FontColor(Oro);
                        x.Item().AlignRight().Text(DateTime.Now.ToString("d 'de' MMMM 'de' yyyy", Mx)).FontSize(8).FontColor("#BDBDBD");
                    });
                });
                col.Item().Height(3).Background(Oro);
            });
        }

        private void Pie(IContainer contenedor)
        {
            contenedor.Column(col =>
            {
                col.Item().LineHorizontal(0.5f).LineColor(GrisLinea);
                col.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text(_grupo.Nombre).FontSize(7).FontColor(GrisTexto);
                    r.ConstantItem(90).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7).FontColor(GrisTexto));
                        t.Span("Página ");
                        t.CurrentPageNumber();
                        t.Span(" de ");
                        t.TotalPages();
                    });
                });
            });
        }

        private static void Titulo(ColumnDescriptor col, string texto)
        {
            col.Item().PaddingTop(16).PaddingBottom(6).Text(texto.ToUpper(Mx)).FontSize(9).Bold().FontColor(OroOscuro);
        }

        private static void TablaDatos(IContainer contenedor, IEnumerable<(string Etiqueta, string Valor)> filas)
        {
            contenedor.Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(140);
                    cd.RelativeColumn();
                });
                foreach (var f in filas)
                {
                    t.Cell().BorderBottom(0.5f).BorderColor(GrisLinea).PaddingVertical(5).Text(f.Etiqueta).FontSize(9).FontColor(GrisTexto);
                    t.Cell().BorderBottom(0.5f).BorderColor(GrisLinea).PaddingVertical(5).Text(f.Valor).SemiBold();
                }
            });
        }

        private static void CajaMontos(ColumnDescriptor col, decimal precio, decimal pagado, decimal saldo)
        {
            col.Item().PaddingTop(16).Background(Crema).Border(0.5f).BorderColor(OroClaro).Padding(14).Row(r =>
            {
                Monto(r.RelativeItem(), "Precio acordado", precio, false);
                Monto(r.RelativeItem(), "Total pagado", pagado, false);
                Monto(r.RelativeItem(), "Saldo pendiente", saldo, true);
            });
        }

        private static void Monto(IContainer contenedor, string etiqueta, decimal valor, bool destacado)
        {
            contenedor.Column(x =>
            {
                x.Item().Text(etiqueta.ToUpper(Mx)).FontSize(7).FontColor(GrisTexto);
                x.Item().Text(valor.ToString("C", Mx)).FontSize(destacado ? 15 : 13).Bold().FontColor(destacado ? OroOscuro : Texto);
            });
        }

        /// <summary>Bloque de firmas. Cuando "conFirma" es true se coloca la firma digitalizada del representante.</summary>
        private void Firmas(ColumnDescriptor col, (string Nombre, string Rol, bool ConFirma) izquierda, (string Nombre, string Rol, bool ConFirma)? derecha)
        {
            col.Item().PaddingTop(36).Row(r =>
            {
                Firma(r.RelativeItem(), izquierda.Nombre, izquierda.Rol, izquierda.ConFirma);
                r.ConstantItem(40);
                if (derecha.HasValue)
                    Firma(r.RelativeItem(), derecha.Value.Nombre, derecha.Value.Rol, derecha.Value.ConFirma);
                else
                    r.RelativeItem();
            });
        }

        private void Firma(IContainer contenedor, string nombre, string rol, bool conFirma)
        {
            contenedor.Column(x =>
            {
                // Espacio para la firma: la imagen si existe, si no un hueco para firmar a mano
                if (conFirma && _firma != null)
                    x.Item().Height(56).AlignCenter().AlignBottom().Image(_firma).FitHeight();
                else
                    x.Item().Height(56);
                x.Item().PaddingHorizontal(16).LineHorizontal(0.75f).LineColor(Texto);
                x.Item().PaddingTop(4).AlignCenter().Text(nombre).SemiBold();
                x.Item().AlignCenter().Text(rol).FontSize(8).FontColor(GrisTexto);
            });
        }

        // =====================================================================
        //  UTILIDADES
        // =====================================================================
        private static string Duracion(Evento ev)
        {
            var d = ev.Duracion;
            var h = (int)d.TotalHours;
            return d.Minutes == 0 ? $"{h} hora{(h == 1 ? "" : "s")}" : $"{h} h {d.Minutes} min";
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Mx) + s.Substring(1);

        /// <summary>Convierte un importe a letra: 10500.00 → "DIEZ MIL QUINIENTOS PESOS 00/100 M.N."</summary>
        public static string Letras(decimal monto)
        {
            var entero = (long)Math.Floor(monto);
            var centavos = (int)Math.Round((monto - entero) * 100);
            if (centavos == 100) { entero++; centavos = 0; }
            var texto = entero == 0 ? "CERO" : Apocopar(Convertir(entero));
            var moneda = entero == 1 ? "PESO" : "PESOS";
            if (entero >= 1_000_000 && entero % 1_000_000 == 0) moneda = "DE PESOS";
            return $"{texto} {moneda} {centavos:00}/100 M.N.";
        }

        private static readonly string[] Unidades =
        {
            "", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
            "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE",
            "VEINTE", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"
        };

        private static readonly string[] DecenasTxt =
            { "", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" };

        private static readonly string[] CentenasTxt =
            { "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS" };

        private static string Apocopar(string s)
        {
            if (s.EndsWith("VEINTIUNO")) return s.Substring(0, s.Length - 9) + "VEINTIÚN";
            if (s.EndsWith("UNO")) return s.Substring(0, s.Length - 3) + "UN";
            return s;
        }

        private static string Convertir(long n)
        {
            if (n == 0) return "";
            if (n < 1000) return Centenas((int)n);
            if (n < 1_000_000)
            {
                var miles = n / 1000;
                var resto = (int)(n % 1000);
                var pref = miles == 1 ? "MIL" : Apocopar(Centenas((int)miles)) + " MIL";
                return (pref + " " + Centenas(resto)).Trim();
            }
            var millones = n / 1_000_000;
            var r = n % 1_000_000;
            var pm = millones == 1 ? "UN MILLÓN" : Apocopar(Convertir(millones)) + " MILLONES";
            return (pm + " " + Convertir(r)).Trim();
        }

        private static string Centenas(int n)
        {
            if (n == 0) return "";
            if (n == 100) return "CIEN";
            var c = n / 100;
            var d = n % 100;
            return (CentenasTxt[c] + " " + Decenas(d)).Trim();
        }

        private static string Decenas(int n)
        {
            if (n < 30) return Unidades[n];
            var d = n / 10;
            var u = n % 10;
            return u == 0 ? DecenasTxt[d] : DecenasTxt[d] + " Y " + Unidades[u];
        }
    }
}
