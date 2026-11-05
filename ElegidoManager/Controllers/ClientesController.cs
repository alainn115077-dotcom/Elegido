using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolesEquipo)]
    public class ClientesController : Controller
    {
        private readonly AppDbContext _db;
        public ClientesController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index(string? q)
        {
            var consulta = _db.Clientes.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLower();
                consulta = consulta.Where(c => c.NombreCompleto.ToLower().Contains(t) || (c.Telefono ?? "").Contains(t) || (c.Ciudad ?? "").ToLower().Contains(t));
            }

            var lista = await consulta
                .OrderBy(c => c.NombreCompleto)
                .Select(c => new ClienteFila
                {
                    Id = c.Id,
                    NombreCompleto = c.NombreCompleto,
                    Telefono = c.Telefono,
                    Ciudad = c.Ciudad,
                    Contrataciones = c.Contrataciones.Count(x => x.Estado != EstadoContratacion.Cancelada),
                    // Dos agregados separados (SQL Server no permite SUM sobre una subconsulta)
                    Saldo = c.Contrataciones.Where(x => x.Estado != EstadoContratacion.Cancelada).Sum(x => x.PrecioAcordado)
                          - c.Contrataciones.Where(x => x.Estado != EstadoContratacion.Cancelada).SelectMany(x => x.Pagos).Sum(p => p.Monto),
                    UltimoEvento = c.Contrataciones.Where(x => x.Evento != null).Max(x => (DateTime?)x.Evento!.Inicio)
                })
                .ToListAsync();

            ViewBag.Q = q;
            return View(lista);
        }

        public async Task<IActionResult> Detalle(int id)
        {
            var cliente = await _db.Clientes
                .Include(c => c.Contrataciones).ThenInclude(x => x.Evento)
                .Include(c => c.Contrataciones).ThenInclude(x => x.Pagos)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (cliente == null) return NotFound();
            return View(cliente);
        }

        public IActionResult Crear() => View("Form", new Cliente());

        [HttpPost]
        public async Task<IActionResult> Crear(Cliente cliente)
        {
            await ValidarDuplicado(cliente);
            if (!ModelState.IsValid) return View("Form", cliente);

            cliente.FechaRegistro = DateTime.Now;
            cliente.Telefono = Formato.Telefono(cliente.Telefono);
            _db.Clientes.Add(cliente);
            await _db.SaveChangesAsync();
            TempData["Exito"] = $"Cliente \"{cliente.NombreCompleto}\" registrado.";
            return RedirectToAction(nameof(Detalle), new { id = cliente.Id });
        }

        public async Task<IActionResult> Editar(int id)
        {
            var cliente = await _db.Clientes.FindAsync(id);
            if (cliente == null) return NotFound();
            return View("Form", cliente);
        }

        [HttpPost]
        public async Task<IActionResult> Editar(int id, Cliente cliente)
        {
            if (id != cliente.Id) return BadRequest();
            await ValidarDuplicado(cliente);
            if (!ModelState.IsValid) return View("Form", cliente);

            var actual = await _db.Clientes.FindAsync(id);
            if (actual == null) return NotFound();
            actual.NombreCompleto = cliente.NombreCompleto.Trim();
            actual.Telefono = Formato.Telefono(cliente.Telefono);
            actual.Ciudad = cliente.Ciudad?.Trim();
            actual.Correo = cliente.Correo?.Trim();
            actual.Notas = cliente.Notas;
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Datos del cliente actualizados.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        /// <summary>Elimina al cliente definitivamente junto con sus contrataciones, eventos, pagos y documentos.</summary>
        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            var cliente = await _db.Clientes
                .Include(c => c.Contrataciones).ThenInclude(x => x.Pagos)
                .Include(c => c.Contrataciones).ThenInclude(x => x.Documentos)
                .Include(c => c.Contrataciones).ThenInclude(x => x.Gastos)
                .Include(c => c.Contrataciones).ThenInclude(x => x.Evento)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (cliente == null) return NotFound();

            await using var tx = await _db.Database.BeginTransactionAsync();
            foreach (var con in cliente.Contrataciones)
            {
                _db.Documentos.RemoveRange(con.Documentos);
                _db.Pagos.RemoveRange(con.Pagos);
                foreach (var g in con.Gastos) g.ContratacionId = null;
                if (con.Evento != null) _db.Eventos.Remove(con.Evento);
                _db.Contrataciones.Remove(con);
            }
            _db.Clientes.Remove(cliente);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Exito"] = $"Cliente \"{cliente.NombreCompleto}\" eliminado definitivamente.";
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidarDuplicado(Cliente c)
        {
            if (string.IsNullOrWhiteSpace(c.Telefono)) return;
            var digitos = Formato.SoloDigitos(c.Telefono);
            var existe = await _db.Clientes.AnyAsync(x => x.Telefono.Replace(" ", "").Replace("-", "") == digitos && x.Id != c.Id);
            if (existe) ModelState.AddModelError(nameof(c.Telefono), "Ya existe un cliente con este teléfono.");
        }
    }

    public class ClienteFila
    {
        public int Id { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string? Ciudad { get; set; }
        public int Contrataciones { get; set; }
        public decimal Saldo { get; set; }
        public DateTime? UltimoEvento { get; set; }
    }
}
