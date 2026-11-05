using ElegidoManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElegidoManager.Controllers
{
    /// <summary>Webhook que usa Meta (WhatsApp Cloud API) para entregar los mensajes de los clientes.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    [Route("api/whatsapp")]
    public class WhatsAppController : Controller
    {
        private readonly BotWhatsApp _bot;
        private readonly ILogger<WhatsAppController> _log;
        public WhatsAppController(BotWhatsApp bot, ILogger<WhatsAppController> log) { _bot = bot; _log = log; }

        /// <summary>Verificación del webhook: Meta manda el token que tú inventaste y espera el "challenge" de vuelta.</summary>
        [HttpGet]
        public IActionResult Verificar([FromQuery(Name = "hub.mode")] string? modo,
                                       [FromQuery(Name = "hub.verify_token")] string? token,
                                       [FromQuery(Name = "hub.challenge")] string? reto)
        {
            var esperado = _bot.Ajustes.VerifyToken;
            if (modo == "subscribe" && !string.IsNullOrEmpty(esperado) && token == esperado) return Content(reto ?? "");
            return StatusCode(403);
        }

        /// <summary>Mensajes entrantes. Siempre responde 200 para que Meta no reintente sin fin.</summary>
        [HttpPost]
        public async Task<IActionResult> Recibir()
        {
            using var lector = new StreamReader(Request.Body);
            var cuerpo = await lector.ReadToEndAsync();
            if (!_bot.FirmaValida(cuerpo, Request.Headers["X-Hub-Signature-256"]))
            {
                _log.LogWarning("Webhook de WhatsApp con firma inválida: se ignoró.");
                return Unauthorized();
            }
            try { await _bot.ProcesarAsync(cuerpo); }
            catch (Exception ex) { _log.LogError(ex, "Error al procesar un mensaje de WhatsApp."); }
            return Ok();
        }
    }
}
