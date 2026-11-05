using System.Security.Claims;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    public class CuentaController : Controller
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher<Usuario> _hasher = new();

        public CuentaController(AppDbContext db) => _db = db;

        [AllowAnonymous, HttpGet]
        public async Task<IActionResult> Login(string? returnUrl)
        {
            if (!await _db.Usuarios.AnyAsync()) return RedirectToAction(nameof(Configurar));
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        private const int MaxIntentos = 5;
        private static readonly TimeSpan TiempoBloqueo = TimeSpan.FromMinutes(15);

        [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var nombre = vm.Usuario.Trim().ToLower();
            var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario.ToLower() == nombre);

            if (usuario != null && usuario.BloqueadoHasta > DateTime.Now)
            {
                var minutos = (int)Math.Ceiling((usuario.BloqueadoHasta.Value - DateTime.Now).TotalMinutes);
                ModelState.AddModelError(string.Empty, $"Cuenta bloqueada por seguridad. Intenta de nuevo en {minutos} minuto(s).");
                return View(vm);
            }

            if (usuario == null || !usuario.Activo ||
                _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, vm.Contrasena) == PasswordVerificationResult.Failed)
            {
                if (usuario != null && usuario.Activo)
                {
                    usuario.IntentosFallidos++;
                    if (usuario.IntentosFallidos >= MaxIntentos)
                    {
                        usuario.IntentosFallidos = 0;
                        usuario.BloqueadoHasta = DateTime.Now.Add(TiempoBloqueo);
                        _db.Bitacoras.Add(_db.NuevaBitacora("Cuenta bloqueada", $"{MaxIntentos} intentos fallidos seguidos", usuario.NombreUsuario));
                    }
                }
                _db.Bitacoras.Add(_db.NuevaBitacora("Intento fallido", "Usuario o contraseña incorrectos", vm.Usuario.Trim()));
                await _db.SaveChangesAsync();
                ModelState.AddModelError(string.Empty, usuario?.BloqueadoHasta > DateTime.Now
                    ? $"Demasiados intentos. Tu cuenta quedó bloqueada {TiempoBloqueo.TotalMinutes:0} minutos."
                    : "Usuario o contraseña incorrectos.");
                return View(vm);
            }

            usuario.UltimoAcceso = DateTime.Now;
            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            if (string.IsNullOrEmpty(usuario.SelloSeguridad)) usuario.SelloSeguridad = Guid.NewGuid().ToString("N");
            _db.Bitacoras.Add(_db.NuevaBitacora("Inició sesión", null, usuario.NombreUsuario));
            await _db.SaveChangesAsync();
            await IniciarSesionAsync(usuario, vm.Recordarme);

            if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return LocalRedirect(vm.ReturnUrl);
            return RedirectToAction("Index", "Home");
        }

        /// <summary>Primer arranque: crea la cuenta del administrador cuando la tabla Usuarios está vacía.</summary>
        [AllowAnonymous, HttpGet]
        public async Task<IActionResult> Configurar()
        {
            if (await _db.Usuarios.AnyAsync()) return RedirectToAction(nameof(Login));
            return View(new NuevoUsuarioViewModel());
        }

        [AllowAnonymous, HttpPost]
        public async Task<IActionResult> Configurar(NuevoUsuarioViewModel vm)
        {
            if (await _db.Usuarios.AnyAsync()) return RedirectToAction(nameof(Login));
            if (!ModelState.IsValid) return View(vm);

            var usuario = new Usuario
            {
                NombreUsuario = vm.Usuario.Trim(),
                NombreCompleto = vm.NombreCompleto.Trim(),
                Rol = Catalogos.RolAdministrador,
                Activo = true,
                UltimoAcceso = DateTime.Now,
                SelloSeguridad = Guid.NewGuid().ToString("N")
            };
            usuario.PasswordHash = _hasher.HashPassword(usuario, vm.Contrasena);
            _db.Usuarios.Add(usuario);
            await _db.SaveChangesAsync();

            await IniciarSesionAsync(usuario, false);
            TempData["Exito"] = "¡Bienvenido a ELEGIDO MANAGER! Tu cuenta de administrador quedó creada.";
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> Salir()
        {
            _db.Bitacoras.Add(_db.NuevaBitacora("Cerró sesión", null));
            await _db.SaveChangesAsync();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult CambiarContrasena() => View(new CambiarContrasenaViewModel());

        [HttpPost]
        public async Task<IActionResult> CambiarContrasena(CambiarContrasenaViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var usuario = await _db.Usuarios.FindAsync(id);
            if (usuario == null) return RedirectToAction(nameof(Login));

            if (_hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, vm.Actual) == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(nameof(vm.Actual), "La contraseña actual no es correcta.");
                return View(vm);
            }

            usuario.PasswordHash = _hasher.HashPassword(usuario, vm.Nueva);
            // Nuevo sello: se cierran las sesiones abiertas en otros dispositivos
            usuario.SelloSeguridad = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync();
            await IniciarSesionAsync(usuario, false);
            TempData["Exito"] = "Contraseña actualizada correctamente.";
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public IActionResult AccesoDenegado() => View();

        private async Task IniciarSesionAsync(Usuario u, bool persistente)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, u.Id.ToString()),
                new(ClaimTypes.Name, u.NombreUsuario),
                new("NombreCompleto", u.NombreCompleto),
                new(ClaimTypes.Role, u.Rol),
                new("sello", u.SelloSeguridad ?? "")
            };
            var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identidad),
                new AuthenticationProperties { IsPersistent = persistente });
        }
    }
}
