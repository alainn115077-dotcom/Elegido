using System.Globalization;
using ElegidoManager.Data;
using ElegidoManager.Models;
using ElegidoManager.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = Catalogos.RolAdministrador)]
    public class FinanzasController : Controller
    {
        private static readonly CultureInfo Mx = new("es-MX");
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public FinanzasController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        /// <summary>Montos rápidos por integrante (appsettings → PagosSugeridos), buscando el nombre sin importar acentos ni mayúsculas.</summary>
        private List<decimal> Sugerencias(string nombre)
        {
            var comparador = CultureInfo.InvariantCulture.CompareInfo;
            foreach (var seccion in _config.GetSection("PagosSugeridos").GetChildren())
            {
                if (comparador.Compare(seccion.Key, nombre.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0)
                    return seccion.GetChildren()
                        .Select(x => decimal.TryParse(x.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0)
                        .Where(v => v > 0).ToList();
            }
            return new List<decimal>();
        }

        private static DateTime ParsearMes(string? mes)
        {
            if (!string.IsNullOrEmpty(mes) &&
                DateTime.TryParseExact(mes, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                return m;
            var hoy = DateTime.Today;
            return new DateTime(hoy.Year, hoy.Month, 1);
        }

        // ------------------------------------------------------------- RESUMEN MENSUAL Y MOVIMIENTOS
        public async Task<IActionResult> Index(string? mes)
        {
            var ini = ParsearMes(mes);
            var fin = ini.AddMonths(1);

            var pagos = await _db.Pagos.Where(p => p.Fecha >= ini && p.Fecha < fin)
                .Select(p => new
                {
                    p.Id, p.Fecha, p.Monto, p.Tipo, p.Metodo, p.ContratacionId,
                    Cliente = p.Contratacion.Cliente.NombreCompleto,
                    p.Contratacion.TipoEvento
                }).ToListAsync();

            var gastos = await _db.Gastos.Where(g => g.Fecha >= ini && g.Fecha < fin)
                .Select(g => new { g.Id, g.Fecha, g.Monto, g.Concepto, g.Categoria, g.ContratacionId })
                .ToListAsync();

            var saldos = await _db.Contrataciones.Where(c => c.Estado != EstadoContratacion.Cancelada)
                .Select(c => c.PrecioAcordado - c.Pagos.Sum(p => p.Monto)).ToListAsync();

            var vm = new FinanzasViewModel
            {
                Mes = ini,
                Ingresos = pagos.Sum(p => p.Monto),
                Anticipos = pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto),
                Gastos = gastos.Sum(g => g.Monto),
                PorCobrar = saldos.Where(s => s > 0).Sum(),
                ValorContratadoMes = await _db.Eventos
                    .Where(e => e.Contratacion.Estado != EstadoContratacion.Cancelada && e.Inicio >= ini && e.Inicio < fin)
                    .SumAsync(e => e.Contratacion.PrecioAcordado),
                GastosPorCategoria = gastos.GroupBy(g => g.Categoria)
                    .Select(g => (g.Key, g.Sum(x => x.Monto))).OrderByDescending(x => x.Item2).ToList()
            };

            // Ingresos personales: eventos del mes que ya se trabajaron
            var ahora = DateTime.Now;
            var trabajados = await _db.Eventos
                .Where(e => e.Inicio >= ini && e.Inicio < fin && e.Fin < ahora && e.Contratacion.Estado != EstadoContratacion.Cancelada)
                .Select(e => new
                {
                    e.Contratacion.PrecioAcordado,
                    Anticipo = e.Contratacion.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto),
                    Pagos = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes).Sum(g => g.Monto),
                    Gasolina = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaGasolina).Sum(g => g.Monto),
                    Otros = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaOtro && g.Notas == Catalogos.MarcaPagosEvento).Sum(g => g.Monto),
                    Registrado = e.Contratacion.Gastos.Any(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes || g.Categoria == Catalogos.CategoriaGasolina || g.Notas == Catalogos.MarcaPagosEvento)
                })
                .ToListAsync();
            var conRegistro = trabajados.Where(t => t.Registrado).ToList();
            vm.IngresosGanados = conRegistro.Sum(t => t.PrecioAcordado - t.Anticipo - t.Pagos - t.Gasolina - t.Otros);
            vm.EventosConGanancia = conRegistro.Count;
            // Anticipos recibidos en el mes (cuentan desde que te los dan, aunque el evento sea después)
            var anticiposMes = pagos.Where(p => p.Tipo == TipoPago.Anticipo).ToList();
            vm.AnticiposEjecutados = anticiposMes.Sum(p => p.Monto);
            vm.EventosConAnticipo = anticiposMes.Select(p => p.ContratacionId).Distinct().Count();

            vm.Movimientos.AddRange(pagos.Select(p => new Movimiento
            {
                Fecha = p.Fecha, EsIngreso = true, Monto = p.Monto, ContratacionId = p.ContratacionId, RegistroId = p.Id,
                Concepto = $"{p.Tipo.Nombre()} · {p.Cliente}",
                Detalle = p.Metodo.Nombre()
            }));
            vm.Movimientos.AddRange(gastos.Select(g => new Movimiento
            {
                Fecha = g.Fecha, EsIngreso = false, Monto = g.Monto, ContratacionId = g.ContratacionId, RegistroId = g.Id,
                Concepto = g.Concepto, Detalle = g.Categoria
            }));
            vm.Movimientos = vm.Movimientos.OrderByDescending(m => m.Fecha).ToList();

            return View(vm);
        }

        // ------------------------------------------------------------- POR EVENTO (eventos ya trabajados)
        public async Task<IActionResult> PorEvento()
        {
            var ahora = DateTime.Now;
            var lista = await _db.Eventos
                .Where(e => e.Fin < ahora && e.Contratacion.Estado != EstadoContratacion.Cancelada)
                .OrderByDescending(e => e.Inicio)
                .Take(80)
                .Select(e => new EventoTrabajado
                {
                    ContratacionId = e.ContratacionId,
                    Cliente = e.Contratacion.Cliente.NombreCompleto,
                    Inicio = e.Inicio,
                    Fin = e.Fin,
                    Lugar = e.Lugar,
                    Direccion = e.Direccion,
                    Ciudad = e.Ciudad,
                    HorarioPendiente = e.HorarioPendiente,
                    Precio = e.Contratacion.PrecioAcordado,
                    Anticipo = e.Contratacion.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto),
                    PagosIntegrantes = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes).Sum(g => g.Monto),
                    Gasolina = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaGasolina).Sum(g => g.Monto),
                    Otros = e.Contratacion.Gastos.Where(g => g.Categoria == Catalogos.CategoriaOtro && g.Notas == Catalogos.MarcaPagosEvento).Sum(g => g.Monto),
                    Registrado = e.Contratacion.Gastos.Any(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes || g.Categoria == Catalogos.CategoriaGasolina || g.Notas == Catalogos.MarcaPagosEvento)
                })
                .ToListAsync();
            return View(lista);
        }

        // ------------------------------------------------------------- PAGOS DE UN EVENTO
        public async Task<IActionResult> Evento(int id)
        {
            var vm = new PagosEventoViewModel { ContratacionId = id };
            if (!await CargarPagosEventoAsync(vm, true)) return NotFound();
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Evento(PagosEventoViewModel vm)
        {
            var c = await _db.Contrataciones
                .Include(x => x.Gastos)
                .Include(x => x.Pagos)
                .Include(x => x.Evento)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == vm.ContratacionId);
            if (c == null || c.Evento == null) return NotFound();

            if (!ModelState.IsValid)
            {
                await CargarPagosEventoAsync(vm, false);
                return View(vm);
            }

            var nombres = await _db.Integrantes.ToDictionaryAsync(i => i.Id, i => i.NombreCompleto);

            // Reemplaza lo capturado antes en esta pantalla (pagos a integrantes y gasolina del evento)
            var anteriores = c.Gastos.Where(g => g.Categoria == Catalogos.CategoriaPagoIntegrantes
                                              || (g.Categoria == Catalogos.CategoriaGasolina && g.Concepto == "Gasolina")
                                              || g.Notas == Catalogos.MarcaPagosEvento).ToList();
            _db.Gastos.RemoveRange(anteriores);

            var fecha = c.Evento.Inicio.Date;
            foreach (var fila in vm.Integrantes.Where(f => f.Monto.HasValue && f.Monto.Value > 0 && nombres.ContainsKey(f.IntegranteId)))
            {
                _db.Gastos.Add(new Gasto
                {
                    Concepto = "Pago a " + nombres[fila.IntegranteId],
                    Categoria = Catalogos.CategoriaPagoIntegrantes,
                    Monto = fila.Monto!.Value,
                    Fecha = fecha,
                    ContratacionId = c.Id,
                    IntegranteId = fila.IntegranteId,
                    Notas = Catalogos.MarcaPagosEvento
                });
            }
            if (vm.Gasolina.HasValue && vm.Gasolina.Value > 0)
            {
                _db.Gastos.Add(new Gasto
                {
                    Concepto = "Gasolina",
                    Categoria = Catalogos.CategoriaGasolina,
                    Monto = vm.Gasolina.Value,
                    Fecha = fecha,
                    ContratacionId = c.Id,
                    Notas = Catalogos.MarcaPagosEvento
                });
            }
            if (vm.OtroMonto.HasValue && vm.OtroMonto.Value > 0)
            {
                _db.Gastos.Add(new Gasto
                {
                    Concepto = string.IsNullOrWhiteSpace(vm.OtroConcepto) ? "Otro" : vm.OtroConcepto.Trim(),
                    Categoria = Catalogos.CategoriaOtro,
                    Monto = vm.OtroMonto.Value,
                    Fecha = fecha,
                    ContratacionId = c.Id,
                    Notas = Catalogos.MarcaPagosEvento
                });
            }

            // El cliente terminó de pagar: se registra el saldo como liquidación
            var saldo = c.PrecioAcordado - c.Pagos.Sum(p => p.Monto);
            if (vm.ClientePagoResto && saldo > 0)
            {
                _db.Pagos.Add(new Pago
                {
                    ContratacionId = c.Id,
                    Monto = saldo,
                    Tipo = TipoPago.Liquidacion,
                    Metodo = MetodoPago.Efectivo,
                    Fecha = DateTime.Now,
                    Notas = "Registrado desde Finanzas → Por evento",
                    FechaRegistro = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();

            var anticipo = c.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto);
            var pagos = vm.Integrantes.Sum(f => f.Monto ?? 0);
            var ganancia = c.PrecioAcordado - anticipo - pagos - (vm.Gasolina ?? 0) - (vm.OtroMonto ?? 0);
            TempData["Exito"] = $"Pagos del evento guardados. Tu ganancia: {ganancia.ToString("C0", Mx)}.";
            return RedirectToAction(nameof(Evento), new { id = c.Id });
        }

        /// <summary>Llena los datos del evento; si "conMontos" es true también trae lo que ya se había guardado.</summary>
        private async Task<bool> CargarPagosEventoAsync(PagosEventoViewModel vm, bool conMontos)
        {
            var datos = await _db.Contrataciones
                .Where(c => c.Id == vm.ContratacionId && c.Evento != null)
                .Select(c => new
                {
                    Cliente = c.Cliente.NombreCompleto,
                    c.Evento!.Inicio,
                    c.Evento.Fin,
                    c.Evento.Lugar,
                    c.Evento.Direccion,
                    c.Evento.Ciudad,
                    c.PrecioAcordado,
                    Anticipo = c.Pagos.Where(p => p.Tipo == TipoPago.Anticipo).Sum(p => p.Monto),
                    Pagado = c.Pagos.Sum(p => p.Monto)
                })
                .FirstOrDefaultAsync();
            if (datos == null) return false;

            vm.Cliente = datos.Cliente;
            vm.Inicio = datos.Inicio;
            vm.Fin = datos.Fin;
            vm.Ubicacion = string.Join(", ", new[] { datos.Lugar, datos.Direccion, datos.Ciudad }.Where(x => !string.IsNullOrWhiteSpace(x)));
            vm.Precio = datos.PrecioAcordado;
            vm.Anticipo = datos.Anticipo;
            vm.Pagado = datos.Pagado;

            var integrantes = await _db.Integrantes.Where(i => i.Activo).OrderBy(i => i.Id)
                .Select(i => new { i.Id, i.NombreCompleto }).ToListAsync();

            if (conMontos)
            {
                var gastos = await _db.Gastos.Where(g => g.ContratacionId == vm.ContratacionId).ToListAsync();
                vm.Integrantes = integrantes.Select(i =>
                {
                    var pagado = gastos.Where(g => g.IntegranteId == i.Id && g.Categoria == Catalogos.CategoriaPagoIntegrantes).Sum(g => g.Monto);
                    return new PagoIntegranteFila { IntegranteId = i.Id, Nombre = i.NombreCompleto, Monto = pagado > 0 ? pagado : null, Sugerencias = Sugerencias(i.NombreCompleto) };
                }).ToList();
                var gas = gastos.Where(g => g.Categoria == Catalogos.CategoriaGasolina && g.Concepto == "Gasolina").Sum(g => g.Monto);
                vm.Gasolina = gas > 0 ? gas : null;
                var otro = gastos.FirstOrDefault(g => g.Categoria == Catalogos.CategoriaOtro && g.Notas == Catalogos.MarcaPagosEvento);
                if (otro != null) { vm.OtroConcepto = otro.Concepto; vm.OtroMonto = otro.Monto; }
                vm.ClientePagoResto = vm.Saldo > 0;
            }
            else
            {
                foreach (var f in vm.Integrantes)
                {
                    f.Nombre = integrantes.FirstOrDefault(i => i.Id == f.IntegranteId)?.NombreCompleto ?? "";
                    f.Sugerencias = Sugerencias(f.Nombre);
                }
            }
            return true;
        }

        // ------------------------------------------------------------- GASTOS
        public async Task<IActionResult> Gastos(string? mes, string? categoria)
        {
            var consulta = _db.Gastos.Include(g => g.Contratacion).ThenInclude(c => c!.Cliente).AsQueryable();
            DateTime? ini = null;
            if (!string.IsNullOrEmpty(mes))
            {
                ini = ParsearMes(mes);
                var i = ini.Value; var f = i.AddMonths(1);
                consulta = consulta.Where(g => g.Fecha >= i && g.Fecha < f);
            }
            if (!string.IsNullOrEmpty(categoria)) consulta = consulta.Where(g => g.Categoria == categoria);

            ViewData["Mes"] = ini;
            ViewData["Categoria"] = categoria;
            return View(await consulta.OrderByDescending(g => g.Fecha).Take(500).ToListAsync());
        }

        public async Task<IActionResult> CrearGasto(int? contratacionId)
        {
            await CargarEventosAsync(contratacionId);
            return View("GastoForm", new Gasto { ContratacionId = contratacionId, Fecha = DateTime.Today });
        }

        [HttpPost]
        public async Task<IActionResult> CrearGasto(Gasto gasto, bool volverAEvento = false)
        {
            ValidarGasto(gasto);
            if (!ModelState.IsValid)
            {
                await CargarEventosAsync(gasto.ContratacionId);
                return View("GastoForm", gasto);
            }
            gasto.Concepto = gasto.Concepto.Trim();
            _db.Gastos.Add(gasto);
            await _db.SaveChangesAsync();
            TempData["Exito"] = $"Gasto de {gasto.Monto.ToString("C", Mx)} registrado.";
            if (volverAEvento && gasto.ContratacionId.HasValue)
                return RedirectToAction("Detalle", "Contrataciones", new { id = gasto.ContratacionId });
            return RedirectToAction(nameof(Gastos));
        }

        public async Task<IActionResult> EditarGasto(int id)
        {
            var gasto = await _db.Gastos.FindAsync(id);
            if (gasto == null) return NotFound();
            await CargarEventosAsync(gasto.ContratacionId);
            return View("GastoForm", gasto);
        }

        [HttpPost]
        public async Task<IActionResult> EditarGasto(int id, Gasto gasto)
        {
            if (id != gasto.Id) return BadRequest();
            ValidarGasto(gasto);
            if (!ModelState.IsValid)
            {
                await CargarEventosAsync(gasto.ContratacionId);
                return View("GastoForm", gasto);
            }
            var actual = await _db.Gastos.FindAsync(id);
            if (actual == null) return NotFound();
            actual.Concepto = gasto.Concepto.Trim();
            actual.Categoria = gasto.Categoria;
            actual.Monto = gasto.Monto;
            actual.Fecha = gasto.Fecha;
            actual.ContratacionId = gasto.ContratacionId;
            actual.Notas = gasto.Notas;
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Gasto actualizado.";
            return RedirectToAction(nameof(Gastos));
        }

        [HttpPost]
        public async Task<IActionResult> EliminarGasto(int id)
        {
            var gasto = await _db.Gastos.FindAsync(id);
            if (gasto == null) return NotFound();
            _db.Gastos.Remove(gasto);
            await _db.SaveChangesAsync();
            TempData["Exito"] = "Gasto eliminado.";
            return RedirectToAction(nameof(Gastos));
        }

        private void ValidarGasto(Gasto g)
        {
            if (!string.IsNullOrEmpty(g.Categoria) && !Catalogos.CategoriasGasto.Contains(g.Categoria))
                ModelState.AddModelError(nameof(g.Categoria), "Categoría no válida.");
        }

        private async Task CargarEventosAsync(int? seleccionado)
        {
            var desde = DateTime.Today.AddMonths(-6);
            var eventos = await _db.Eventos
                .Where(e => e.Inicio >= desde || e.ContratacionId == seleccionado)
                .OrderByDescending(e => e.Inicio)
                .Select(e => new { e.ContratacionId, e.Inicio, e.Contratacion.TipoEvento, Cliente = e.Contratacion.Cliente.NombreCompleto })
                .ToListAsync();
            ViewData["Eventos"] = eventos.Select(e => new SelectListItem
            {
                Value = e.ContratacionId.ToString(),
                Text = $"{e.Inicio.ToString("d MMM yyyy", Mx)} · {e.Cliente}",
                Selected = e.ContratacionId == seleccionado
            }).ToList();
        }
    }
}
