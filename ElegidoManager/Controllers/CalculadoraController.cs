using ElegidoManager.Models;
using Microsoft.AspNetCore.Mvc;

namespace ElegidoManager.Controllers
{
    /// <summary>Calculadora rápida para sacar cuentas con los clientes (no guarda nada).</summary>
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class CalculadoraController : Controller
    {
        public IActionResult Index() => View();
    }
}
