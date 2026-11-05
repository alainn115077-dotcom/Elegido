using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        public HomeController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var ahora = DateTime.Now;

            var proximos = await _db.Eventos
                .Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Fin >= ahora)
                .OrderBy(e => e.Inicio)
                .Take(30)
                .Select(e => new EventoResumen
                {
                    ContratacionId = e.ContratacionId,
                    Cliente = e.Contratacion.Cliente.NombreCompleto,
                    Telefono = e.Contratacion.Cliente.Telefono,
                    TipoEvento = e.Contratacion.TipoEvento,
                    Inicio = e.Inicio,
                    Fin = e.Fin,
                    Lugar = e.Lugar,
                    Direccion = e.Direccion,
                    Ciudad = e.Ciudad,
                    Estado = e.Contratacion.Estado,
                    HorarioPendiente = e.HorarioPendiente,
                    Precio = e.Contratacion.PrecioAcordado,
                    Pagado = e.Contratacion.Pagos.Sum(p => p.Monto)
                })
                .ToListAsync();

            var vm = new DashboardViewModel
            {
                Proximos = proximos,
                SinDatos = !await _db.Contrataciones.AnyAsync()
            };
            return View(vm);
        }

        [AllowAnonymous]
        public IActionResult Error() => View();
    }
}
