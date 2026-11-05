using ElegidoManager.Models;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace ElegidoManager.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class ConsultasController : Controller
    {
        private readonly ConsultasService _consultas;
        private readonly ILogger<ConsultasController> _log;

        public ConsultasController(ConsultasService consultas, ILogger<ConsultasController> log)
        {
            _consultas = consultas;
            _log = log;
        }

        public IActionResult Index() => View(ConsultasService.Preguntas);

        /// <summary>Ejecuta la consulta SQL asociada a una pregunta predefinida y devuelve la respuesta en JSON.</summary>
        [HttpGet]
        public async Task<IActionResult> Responder(string clave, DateTime? fecha, string? mes)
        {
            try
            {
                var respuesta = await _consultas.ResponderAsync(clave, fecha, mes, Url);
                return Json(respuesta);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error en consulta rápida {Clave}", clave);
                return StatusCode(500, new { titulo = "No se pudo consultar la base de datos", parrafos = new[] { "Revisa la conexión con SQL Server e inténtalo de nuevo." }, tono = "alerta" });
            }
        }
    }
}
