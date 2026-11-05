using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElegidoManager.Data;
using ElegidoManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElegidoManager.Services
{
    /// <summary>Ajustes del bot (appsettings "WhatsAppBot" o variables WhatsAppBot__AccessToken, etc.).</summary>
    public class WhatsAppBotSettings
    {
        /// <summary>Token de acceso de la app de Meta (WhatsApp Cloud API).</summary>
        public string AccessToken { get; set; } = string.Empty;
        /// <summary>Identificador del número de WhatsApp en Meta (Phone number ID).</summary>
        public string PhoneNumberId { get; set; } = string.Empty;
        /// <summary>Palabra secreta que tú inventas para que Meta verifique el webhook.</summary>
        public string VerifyToken { get; set; } = string.Empty;
        /// <summary>App Secret de Meta: comprueba que los mensajes vienen de verdad de WhatsApp.</summary>
        public string AppSecret { get; set; } = string.Empty;
        public string VersionApi { get; set; } = "v21.0";

        public bool Configurado => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
    }

    /// <summary>
    /// Bot de WhatsApp SIN inteligencia artificial: menú de opciones con respuestas basadas en la agenda real.
    /// Los clientes pueden ver fechas libres, precios y pedir un evento (queda como solicitud para aprobar).
    /// </summary>
    public class BotWhatsApp
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        private readonly AgendaService _agenda;
        private readonly WhatsAppBotSettings _ajustes;
        private readonly GrupoSettings _grupo;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<BotWhatsApp> _log;

        public BotWhatsApp(AppDbContext db, AgendaService agenda, IOptions<WhatsAppBotSettings> ajustes,
            IOptions<GrupoSettings> grupo, IConfiguration config, IHttpClientFactory http, ILogger<BotWhatsApp> log)
        {
            _db = db; _agenda = agenda; _ajustes = ajustes.Value; _grupo = grupo.Value; _config = config; _http = http; _log = log;
        }

        public WhatsAppBotSettings Ajustes => _ajustes;

        // ================================================================ Seguridad del webhook
        /// <summary>Comprueba la firma X-Hub-Signature-256 que pone Meta en cada aviso.</summary>
        public bool FirmaValida(string cuerpo, string? firma)
        {
            if (string.IsNullOrWhiteSpace(_ajustes.AppSecret)) return true; // sin App Secret configurado no se puede verificar
            if (string.IsNullOrWhiteSpace(firma) || !firma.StartsWith("sha256=")) return false;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_ajustes.AppSecret));
            var esperado = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(cuerpo))).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperado), Encoding.UTF8.GetBytes(firma.ToLowerInvariant()));
        }

        // ================================================================ Entrada
        /// <summary>Procesa el JSON que manda Meta y contesta cada mensaje de texto.</summary>
        public async Task ProcesarAsync(string cuerpo)
        {
            using var json = JsonDocument.Parse(cuerpo);
            if (!json.RootElement.TryGetProperty("entry", out var entradas)) return;
            foreach (var entrada in entradas.EnumerateArray())
            foreach (var cambio in entrada.GetProperty("changes").EnumerateArray())
            {
                if (!cambio.TryGetProperty("value", out var valor) || !valor.TryGetProperty("messages", out var mensajes)) continue;
                string? perfil = null;
                if (valor.TryGetProperty("contacts", out var contactos) && contactos.GetArrayLength() > 0 &&
                    contactos[0].TryGetProperty("profile", out var p) && p.TryGetProperty("name", out var n))
                    perfil = n.GetString();

                foreach (var m in mensajes.EnumerateArray())
                {
                    var de = m.GetProperty("from").GetString() ?? "";
                    var tipo = m.GetProperty("type").GetString();
                    var texto = tipo switch
                    {
                        "text" => m.GetProperty("text").GetProperty("body").GetString(),
                        "interactive" => m.GetProperty("interactive").TryGetProperty("button_reply", out var b) ? b.GetProperty("id").GetString()
                                       : m.GetProperty("interactive").TryGetProperty("list_reply", out var l) ? l.GetProperty("id").GetString() : null,
                        "button" => m.GetProperty("button").GetProperty("text").GetString(),
                        _ => null
                    };
                    var respuesta = texto == null
                        ? "Por ahora solo puedo leer mensajes de texto 🙂\n\n" + Menu()
                        : await ResponderAsync(de, perfil, texto);
                    if (respuesta != null) await EnviarAsync(de, respuesta);
                }
            }
        }

        // ================================================================ Lógica del menú
        /// <summary>Calcula la respuesta para un mensaje (también se usa en el simulador de la app).</summary>
        public async Task<string?> ResponderAsync(string telefono, string? perfil, string texto)
        {
            var t = Normalizar(texto);
            var conv = await _db.ConversacionesBot.FirstOrDefaultAsync(c => c.Telefono == telefono);
            if (conv == null)
            {
                conv = new ConversacionBot { Telefono = telefono, NombrePerfil = perfil, Paso = "nuevo" };
                _db.ConversacionesBot.Add(conv);
            }
            if (!string.IsNullOrWhiteSpace(perfil)) conv.NombrePerfil = perfil;

            // Si pidió hablar con Alain, el bot se queda callado un rato (salvo que escriba "menu")
            if (conv.PausadoHasta > DateTime.Now && t != "menu" && t != "0")
            {
                conv.UltimoMensaje = DateTime.Now;
                await _db.SaveChangesAsync();
                return null;
            }

            // Conversación vieja: empieza de nuevo
            if (conv.UltimoMensaje < DateTime.Now.AddHours(-12)) conv.Paso = "nuevo";
            conv.UltimoMensaje = DateTime.Now;

            string respuesta;
            if (t is "0" or "menu" or "inicio" or "hola" or "buenas" or "buen dia" or "buenas tardes" or "buenas noches" || conv.Paso == "nuevo")
            {
                Reiniciar(conv);
                respuesta = Saludo(conv) + "\n\n" + Menu();
            }
            else
            {
                respuesta = conv.Paso switch
                {
                    "fecha" => await PasoFechaAsync(conv, t),
                    "hora" => await PasoHoraAsync(conv, t),
                    "duracion" => PasoDuracion(conv, t),
                    "direccion" => PasoDireccion(conv, texto.Trim()),
                    "nombre" => PasoNombre(conv, texto.Trim()),
                    "confirmar" => await PasoConfirmarAsync(conv, t),
                    _ => await OpcionMenuAsync(conv, t)
                };
            }
            await _db.SaveChangesAsync();
            return respuesta;
        }

        private static void Reiniciar(ConversacionBot c)
        {
            c.Paso = "menu"; c.Fecha = null; c.Hora = null; c.DuracionHoras = null; c.Direccion = null; c.Nombre = null; c.PausadoHasta = null;
        }

        private string Saludo(ConversacionBot c)
        {
            var nombre = (c.NombrePerfil ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return $"¡Hola{(string.IsNullOrEmpty(nombre) ? "" : " " + nombre)}! 🎶 Gracias por escribir a *{_grupo.Nombre}*.";
        }

        private static string Menu() =>
            "Responde con el *número* de lo que necesitas:\n\n" +
            "1️⃣ Ver fechas disponibles\n" +
            "2️⃣ Precios\n" +
            "3️⃣ Agendar un evento\n" +
            "4️⃣ Hablar con una persona\n\n" +
            "Escribe *0* en cualquier momento para volver a este menú.";

        private async Task<string> OpcionMenuAsync(ConversacionBot c, string t)
        {
            switch (t)
            {
                case "1": return await FechasDisponiblesAsync() + "\n\nEscribe *3* para agendar o *0* para el menú.";
                case "2": return Precios() + "\n\nEscribe *3* para agendar o *0* para el menú.";
                case "3":
                    c.Paso = "fecha";
                    return "¡Con gusto! 📅 ¿Para qué *fecha* es tu evento?\nEscríbela así: *20/12* o *20 de diciembre*.";
                case "4":
                    c.PausadoHasta = DateTime.Now.AddHours(12);
                    c.Paso = "menu";
                    return "Perfecto, en un momento te atiende una persona del grupo. 🙌\n(Si quieres volver al menú automático escribe *menu*.)";
                default:
                    return "No entendí tu respuesta 🙈\n\n" + Menu();
            }
        }

        private string Precios()
        {
            var hora = Decimal("Tarifas:PrecioPorHora", 3500);
            var tuba = Decimal("Tarifas:PrecioPorHoraTuba", 4300);
            var anticipo = Decimal("Tarifas:AnticipoSugerido", 500);
            return "💰 *Precios*\n" +
                   $"• Por hora: *{hora.ToString("C0", Mx)}*\n" +
                   $"• Por hora con tuba: *{tuba.ToString("C0", Mx)}*\n" +
                   $"• Ejemplo 2 horas: {(hora * 2).ToString("C0", Mx)} (con tuba {(tuba * 2).ToString("C0", Mx)})\n\n" +
                   $"Para apartar la fecha se deja un anticipo de *{anticipo.ToString("C0", Mx)}* por transferencia.";
        }

        private decimal Decimal(string clave, decimal defecto) =>
            decimal.TryParse(_config[clave], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : defecto;

        private async Task<string> FechasDisponiblesAsync()
        {
            var hoy = DateTime.Today;
            var hasta = hoy.AddDays(60);
            var ocupados = (await _db.Eventos
                    .Where(e => e.Inicio >= hoy && e.Inicio < hasta && e.Contratacion.Estado != EstadoContratacion.Cancelada)
                    .Select(e => e.Inicio).ToListAsync())
                .Select(d => d.Date).ToHashSet();
            var libres = Enumerable.Range(1, 60).Select(i => hoy.AddDays(i))
                .Where(d => d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday && !ocupados.Contains(d))
                .Take(10).ToList();
            if (libres.Count == 0) return "📅 Los próximos fines de semana ya están ocupados. Escribe *4* para hablar con nosotros y buscar opciones.";
            return "📅 *Fines de semana libres* (próximos 2 meses):\n" +
                   string.Join("\n", libres.Select(d => "• " + Cap(d.ToString("dddd d 'de' MMMM", Mx)))) +
                   "\n\nEntre semana también tocamos, pregúntanos. 😉";
        }

        // ---------------------------------------------------------------- Pasos para agendar
        private async Task<string> PasoFechaAsync(ConversacionBot c, string t)
        {
            var fecha = LeerFecha(t);
            if (fecha == null) return "No entendí la fecha 🤔 Escríbela así: *20/12* o *20 de diciembre*.";
            if (fecha.Value.Date < DateTime.Today) return "Esa fecha ya pasó. Escribe otra fecha, por favor.";
            c.Fecha = fecha.Value.Date;
            c.Paso = "hora";

            var delDia = await _agenda.EventosDelDiaAsync(c.Fecha.Value);
            var aviso = delDia.Count == 0
                ? "✅ ¡Ese día está libre!"
                : "ℹ️ Ese día ya tenemos presentación de " + string.Join(", ", delDia.Select(e => e.HorarioPendiente ? "horario por confirmar" : e.Inicio.ToString("h:mm tt", Mx) + " a " + e.Fin.ToString("h:mm tt", Mx))) + ", pero podemos revisar otro horario.";
            return $"{aviso}\n\n⏰ ¿A qué *hora* empezaría? Ejemplo: *9 pm* o *21:00*.\n(Si aún no sabes la hora escribe *pendiente*.)";
        }

        private async Task<string> PasoHoraAsync(ConversacionBot c, string t)
        {
            if (t.StartsWith("pend") || t.Contains("no se"))
            {
                c.Hora = null;
                c.Paso = "duracion";
                return "Está bien, la hora la confirmamos después. ⏳\n¿Cuántas *horas* quieres de música? (1, 2, 3 o 4)";
            }
            var hora = LeerHora(t);
            if (hora == null) return "No entendí la hora 🤔 Escríbela así: *9 pm*, *9:30 pm* o *21:00*.";
            c.Hora = hora;
            c.Paso = "duracion";
            return "¿Cuántas *horas* quieres de música? (1, 2, 3 o 4)";
        }

        private string PasoDuracion(ConversacionBot c, string t)
        {
            var m = Regex.Match(t, @"\d+");
            if (!m.Success || !int.TryParse(m.Value, out var h) || h < 1 || h > 4)
                return "Escribe un número de horas del *1 al 4*, por favor.";
            c.DuracionHoras = h;
            c.Paso = "direccion";
            return "📍 ¿Cuál es la *dirección* o el lugar del evento?\n(Si aún no la tienes escribe *pendiente*.)";
        }

        private string PasoDireccion(ConversacionBot c, string texto)
        {
            if (texto.Length < 3) return "Escribe la dirección o el nombre del lugar, por favor.";
            c.Direccion = Normalizar(texto).StartsWith("pend") ? null : Formato.Direccion(texto.Length > 240 ? texto[..240] : texto);
            c.Paso = "nombre";
            return "👤 Por último, ¿a *nombre* de quién queda el evento?";
        }

        private string PasoNombre(ConversacionBot c, string texto)
        {
            if (texto.Length < 3) return "Escribe tu nombre completo, por favor.";
            c.Nombre = texto.Length > 110 ? texto[..110] : texto;
            c.Paso = "confirmar";
            return Resumen(c) + "\n\n¿Está todo bien?\n1️⃣ Sí, enviar solicitud\n2️⃣ No, empezar de nuevo";
        }

        private async Task<string> PasoConfirmarAsync(ConversacionBot c, string t)
        {
            if (t is "2" or "no")
            {
                Reiniciar(c);
                c.Paso = "fecha";
                return "Va, empecemos de nuevo. 📅 ¿Para qué *fecha* es tu evento?";
            }
            if (t is not ("1" or "si" or "sí")) return "Responde *1* para enviar o *2* para empezar de nuevo.";

            // ¿Choca con otra presentación? Se avisa, pero la solicitud se envía igual para que la revisen
            var choca = false;
            if (c.Hora != null && c.Fecha != null)
            {
                var inicio = c.Fecha.Value.Add(c.Hora.Value);
                choca = (await _agenda.BuscarConflictosAsync(inicio, inicio.AddHours(c.DuracionHoras ?? 1))).Count > 0;
            }

            _db.SolicitudesBot.Add(new SolicitudBot
            {
                Telefono = c.Telefono,
                Nombre = c.Nombre ?? c.NombrePerfil ?? "Cliente de WhatsApp",
                Fecha = c.Fecha!.Value,
                Hora = c.Hora,
                DuracionHoras = c.DuracionHoras ?? 1,
                Direccion = c.Direccion
            });
            var anticipo = Decimal("Tarifas:AnticipoSugerido", 500);
            Reiniciar(c);
            return "✅ ¡Listo! Tu solicitud quedó registrada." +
                   (choca ? "\n⚠️ Ese horario se cruza con otra presentación; lo revisamos y te proponemos opciones." : "") +
                   $"\n\nEn breve te confirmamos por aquí. Para *apartar la fecha* se deja un anticipo de {anticipo.ToString("C0", Mx)} por transferencia. 🙌" +
                   "\n\nEscribe *0* si necesitas algo más.";
        }

        private string Resumen(ConversacionBot c)
        {
            var precioHora = Decimal("Tarifas:PrecioPorHora", 3500);
            var horas = c.DuracionHoras ?? 1;
            var horario = c.Hora == null ? "Por confirmar"
                : $"{DateTime.Today.Add(c.Hora.Value).ToString("h:mm tt", Mx)} a {DateTime.Today.Add(c.Hora.Value).AddHours(horas).ToString("h:mm tt", Mx)}";
            return "📝 *Resumen de tu evento*\n" +
                   $"• Fecha: {Cap(c.Fecha!.Value.ToString("dddd d 'de' MMMM 'de' yyyy", Mx))}\n" +
                   $"• Horario: {horario} ({horas} h)\n" +
                   $"• Lugar: {c.Direccion ?? "Por confirmar"}\n" +
                   $"• A nombre de: {c.Nombre}\n" +
                   $"• Precio estimado: {(precioHora * horas).ToString("C0", Mx)} (sin tuba)";
        }

        // ================================================================ Envío por la API de Meta
        public async Task EnviarAsync(string para, string texto)
        {
            if (!_ajustes.Configurado) { _log.LogWarning("Bot de WhatsApp sin configurar: no se envió la respuesta."); return; }
            var cliente = _http.CreateClient();
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _ajustes.AccessToken);
            var cuerpo = JsonSerializer.Serialize(new
            {
                messaging_product = "whatsapp",
                to = para,
                type = "text",
                text = new { preview_url = false, body = texto }
            });
            var r = await cliente.PostAsync($"https://graph.facebook.com/{_ajustes.VersionApi}/{_ajustes.PhoneNumberId}/messages",
                new StringContent(cuerpo, Encoding.UTF8, "application/json"));
            if (!r.IsSuccessStatusCode)
                _log.LogError("WhatsApp no aceptó el mensaje ({Codigo}): {Detalle}", (int)r.StatusCode, await r.Content.ReadAsStringAsync());
        }

        // ================================================================ Utilidades de texto
        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

        private static string Normalizar(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return Regex.Replace(sb.ToString(), @"[¡!¿?.,]+", "").Trim();
        }

        private static readonly string[] Meses = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

        /// <summary>Lee "20/12", "20-12-2026", "20 de diciembre" o "20 dic". Si ya pasó este año, toma el siguiente.</summary>
        public static DateTime? LeerFecha(string t)
        {
            int dia, mes, anio = 0;
            var m = Regex.Match(t, @"\b(\d{1,2})\s*[/\-.]\s*(\d{1,2})(?:\s*[/\-.]\s*(\d{2,4}))?\b");
            if (m.Success)
            {
                dia = int.Parse(m.Groups[1].Value); mes = int.Parse(m.Groups[2].Value);
                if (m.Groups[3].Success) { anio = int.Parse(m.Groups[3].Value); if (anio < 100) anio += 2000; }
            }
            else
            {
                m = Regex.Match(t, @"\b(\d{1,2})\s*(?:de\s+)?([a-z]{3,})");
                if (!m.Success) return null;
                dia = int.Parse(m.Groups[1].Value);
                var i = Array.FindIndex(Meses, x => m.Groups[2].Value.StartsWith(x) || (x == "sep" && m.Groups[2].Value.StartsWith("set")));
                if (i < 0) return null;
                mes = i + 1;
            }
            if (mes < 1 || mes > 12) return null;
            var hoy = DateTime.Today;
            if (anio == 0)
            {
                anio = hoy.Year;
                if (dia <= DateTime.DaysInMonth(anio, mes) && new DateTime(anio, mes, dia) < hoy) anio++;
            }
            if (dia < 1 || dia > DateTime.DaysInMonth(anio, mes)) return null;
            return new DateTime(anio, mes, dia);
        }

        /// <summary>Lee "9", "9 pm", "9:30 pm", "21:00", "9 de la noche". Sin am/pm, de 1 a 11 se toma como de la tarde/noche.</summary>
        public static TimeSpan? LeerHora(string t)
        {
            var m = Regex.Match(t, @"\b(\d{1,2})(?:\s*[:.]\s*(\d{2}))?\s*(am|a m|pm|p m|de la manana|de la tarde|de la noche|hrs|hr|h)?\b");
            if (!m.Success) return null;
            var h = int.Parse(m.Groups[1].Value);
            var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            var suf = m.Groups[3].Value.Replace(" ", "");
            if (h > 23 || min > 59) return null;
            if (suf is "pm" or "delatarde" or "delanoche" && h < 12) h += 12;
            else if (suf is "am" or "delamanana" && h == 12) h = 0;
            else if (suf == "" && h >= 1 && h <= 11) h += 12; // "9" para una fiesta = 9 de la noche
            return new TimeSpan(h, min, 0);
        }
    }
}
