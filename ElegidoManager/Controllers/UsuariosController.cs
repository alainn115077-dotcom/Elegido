using System.Security.Claims;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    [Authorize(Roles = Catalogos.RolAdministrador)]
    public class UsuariosController : Controller
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher<Usuario> _hasher = new();
        public UsuariosController(AppDbContext db) => _db = db;

        private int IdActual => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            ViewData["IdActual"] = IdActual;
            await CargarIntegrantesAsync();
            return View(await _db.Usuarios.Include(u => u.Integrante).OrderBy(u => u.NombreCompleto).ToListAsync());
        }

        private async Task CargarIntegrantesAsync() =>
            ViewBag.Integrantes = await _db.Integrantes.OrderBy(i => i.NombreCompleto).ToListAsync();

        public async Task<IActionResult> Crear()
        {
            await CargarIntegrantesAsync();
            return View(new NuevoUsuarioViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Crear(NuevoUsuarioViewModel vm)
        {
            if (!Catalogos.Roles.Contains(vm.Rol)) ModelState.AddModelError(nameof(vm.Rol), "Rol no válido.");
            var nombre = vm.Usuario?.Trim() ?? "";
            if (await _db.Usuarios.AnyAsync(u => u.NombreUsuario == nombre))
                ModelState.AddModelError(nameof(vm.Usuario), "Ese nombre de usuario ya existe.");
            if (Catalogos.LigaIntegrante(vm.Rol) && vm.IntegranteId.HasValue && !await _db.Integrantes.AnyAsync(i => i.Id == vm.IntegranteId))
                ModelState.AddModelError(nameof(vm.IntegranteId), "Integrante no válido.");
            if (!ModelState.IsValid)
            {
                await CargarIntegrantesAsync();
                return View(vm);
            }

            var u = new Usuario
            {
                NombreUsuario = nombre, NombreCompleto = vm.NombreCompleto.Trim(), Rol = vm.Rol, Activo = true,
                IntegranteId = Catalogos.LigaIntegrante(vm.Rol) ? vm.IntegranteId : null
            };
            u.PasswordHash = _hasher.HashPassword(u, vm.Contrasena);
            _db.Usuarios.Add(u);
            await _db.SaveChangesAsync();
            TempData["Exito"] = $"Usuario \"{u.NombreUsuario}\" creado.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Liga una cuenta de rol Integrante con el integrante del grupo (para ver sus deudas).</summary>
        [HttpPost]
        public async Task<IActionResult> Vincular(int id, int? integranteId)
        {
            var u = await _db.Usuarios.FindAsync(id);
            if (u == null) return NotFound();
            if (integranteId.HasValue && !await _db.Integrantes.AnyAsync(i => i.Id == integranteId)) return BadRequest();
            u.IntegranteId = integranteId;
            await _db.SaveChangesAsync();
            TempData["Exito"] = integranteId.HasValue ? $"Cuenta \"{u.NombreUsuario}\" ligada al integrante." : $"Cuenta \"{u.NombreUsuario}\" sin integrante ligado.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> CambiarActivo(int id)
        {
            if (id == IdActual)
            {
                TempData["Error"] = "No puedes desactivar tu propia cuenta.";
                return RedirectToAction(nameof(Index));
            }
            var u = await _db.Usuarios.FindAsync(id);
            if (u == null) return NotFound();
            if (u.Activo && u.Rol == Catalogos.RolAdministrador &&
                await _db.Usuarios.CountAsync(x => x.Activo && x.Rol == Catalogos.RolAdministrador) <= 1)
            {
                TempData["Error"] = "Debe quedar al menos un administrador activo.";
                return RedirectToAction(nameof(Index));
            }
            u.Activo = !u.Activo;
            await _db.SaveChangesAsync();
            TempData["Exito"] = u.Activo ? "Usuario activado." : "Usuario desactivado; ya no podrá iniciar sesión.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> RestablecerContrasena(int id, string nueva)
        {
            var u = await _db.Usuarios.FindAsync(id);
            if (u == null) return NotFound();
            if (string.IsNullOrWhiteSpace(nueva) || nueva.Length < 8)
            {
                TempData["Error"] = "La contraseña debe tener al menos 8 caracteres.";
                return RedirectToAction(nameof(Index));
            }
            u.PasswordHash = _hasher.HashPassword(u, nueva);
            await _db.SaveChangesAsync();
            TempData["Exito"] = $"Contraseña de \"{u.NombreUsuario}\" restablecida.";
            return RedirectToAction(nameof(Index));
        }
    }
}
