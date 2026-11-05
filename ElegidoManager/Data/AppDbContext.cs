using System.Collections.Concurrent;
using System.Globalization;
using ElegidoManager.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ElegidoManager.Data
{
    /// <summary>
    /// Contexto de datos. Funciona en dos modos:
    ///  • SQL Server (LocalDB en la PC o Azure SQL): EF Core normal.
    ///  • MongoDB (Atlas): en cada petición se carga la información de Mongo en una base en memoria,
    ///    la aplicación trabaja igual que siempre y al guardar los cambios se escriben en Mongo.
    /// </summary>
    public class AppDbContext : DbContext, IDataProtectionKeyContext
    {
        /// <summary>Se activa al arrancar si hay cadena de conexión de MongoDB.</summary>
        public static bool ModoMongo { get; set; }

        private static readonly ConcurrentBag<string> NombresLibres = new();
        private static int _contadorNombres;

        public MongoAlmacen? Mongo { get; }
        private readonly IHttpContextAccessor? _http;
        private string? _nombreMemoria;
        private bool _cargando;
        private bool _guardandoBitacora;

        public AppDbContext(DbContextOptions<AppDbContext> options, MongoAlmacen? mongo = null, IHttpContextAccessor? http = null)
            : base(options)
        {
            Mongo = mongo;
            _http = http;
        }

        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Cliente> Clientes => Set<Cliente>();
        public DbSet<Servicio> Servicios => Set<Servicio>();
        public DbSet<Contratacion> Contrataciones => Set<Contratacion>();
        public DbSet<Evento> Eventos => Set<Evento>();
        public DbSet<Pago> Pagos => Set<Pago>();
        public DbSet<Gasto> Gastos => Set<Gasto>();
        public DbSet<Integrante> Integrantes => Set<Integrante>();
        public DbSet<MovimientoIntegrante> MovimientosIntegrantes => Set<MovimientoIntegrante>();
        public DbSet<Reunion> Reuniones => Set<Reunion>();
        public DbSet<Cancion> Canciones => Set<Cancion>();
        public DbSet<Documento> Documentos => Set<Documento>();
        public DbSet<Bitacora> Bitacoras => Set<Bitacora>();

        /// <summary>Llaves que firman las cookies de sesión: guardadas en la base para que la sesión no se pierda en la nube.</summary>
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!ModoMongo) return;
            _nombreMemoria ??= NombresLibres.TryTake(out var n) ? n : "em-" + Interlocked.Increment(ref _contadorNombres);
            optionsBuilder.UseInMemoryDatabase(_nombreMemoria)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        }

        // ================================================================ MongoDB: carga
        /// <summary>Carga en memoria toda la información guardada en MongoDB (se llama al crear el contexto).</summary>
        public void CargarDesdeMongo()
        {
            if (!ModoMongo || Mongo == null) return;
            _cargando = true;
            try
            {
                var tipos = Model.GetEntityTypes().Where(t => !MongoAlmacen.SinCargar.Contains(t.ClrType));
                foreach (var entidad in Mongo.LeerVarios(tipos)) Add(entidad);
                base.SaveChanges(true);
            }
            finally { _cargando = false; }
        }

        /// <summary>Contenido de un PDF: en modo Mongo vive aparte para no cargarlo en cada petición.</summary>
        public byte[] ContenidoDocumento(int documentoId, byte[] actual) =>
            actual.Length > 0 || !ModoMongo || Mongo == null ? actual : Mongo.LeerArchivo(documentoId);

        // ================================================================ Guardado (bitácora + Mongo)
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            if (_cargando) return base.SaveChanges(acceptAllChangesOnSuccess);
            ChangeTracker.DetectChanges();
            var auditoria = Auditar();
            var cambios = ModoMongo && Mongo != null ? MongoAlmacen.Capturar(ChangeTracker) : null;
            var n = base.SaveChanges(acceptAllChangesOnSuccess);
            if (cambios != null) Mongo!.Escribir(cambios);
            GuardarBitacora(auditoria, () => SaveChanges(acceptAllChangesOnSuccess));
            return n;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (_cargando) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            ChangeTracker.DetectChanges();
            var auditoria = Auditar();
            var cambios = ModoMongo && Mongo != null ? MongoAlmacen.Capturar(ChangeTracker) : null;
            var n = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (cambios != null) await Mongo!.EscribirAsync(cambios, cancellationToken);
            if (auditoria.Count > 0)
            {
                AgregarBitacora(auditoria);
                _guardandoBitacora = true;
                try { await SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
                finally { _guardandoBitacora = false; }
            }
            return n;
        }

        private void GuardarBitacora(List<(string Accion, string Detalle)> auditoria, Action guardar)
        {
            if (auditoria.Count == 0) return;
            AgregarBitacora(auditoria);
            _guardandoBitacora = true;
            try { guardar(); }
            finally { _guardandoBitacora = false; }
        }

        private static readonly HashSet<string> CamposDeSesion = new()
        {
            nameof(Usuario.UltimoAcceso), nameof(Usuario.IntentosFallidos), nameof(Usuario.BloqueadoHasta), nameof(Usuario.SelloSeguridad)
        };

        /// <summary>Describe los cambios pendientes para la bitácora (antes de guardar, mientras aún existen los datos).</summary>
        private List<(string Accion, string Detalle)> Auditar()
        {
            var lista = new List<(string, string)>();
            if (_guardandoBitacora) return lista;
            foreach (var e in ChangeTracker.Entries())
            {
                if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
                if (e.Entity is Bitacora or DataProtectionKey) continue;
                if (e.State == EntityState.Modified && e.Entity is Usuario &&
                    e.Properties.Where(p => p.IsModified).All(p => CamposDeSesion.Contains(p.Metadata.Name))) continue;

                var verbo = e.State switch { EntityState.Added => "Agregó", EntityState.Deleted => "Eliminó", _ => "Modificó" };
                if (e.Entity is Usuario && e.State == EntityState.Modified && e.Property(nameof(Usuario.PasswordHash)).IsModified)
                    verbo = "Cambió contraseña";
                lista.Add(($"{verbo} {NombreTipo(e.Entity)}", Describir(e.Entity)));
            }
            return lista;
        }

        private void AgregarBitacora(List<(string Accion, string Detalle)> auditoria)
        {
            var principal = auditoria[0].Accion;
            var detalle = string.Join(" · ", auditoria.Select(a => a.Accion == principal ? a.Detalle : $"{a.Accion}: {a.Detalle}").Distinct());
            Bitacoras.Add(NuevaBitacora(principal, detalle));
        }

        /// <summary>Crea un registro de bitácora con el usuario y la IP de la petición actual.</summary>
        public Bitacora NuevaBitacora(string accion, string? detalle, string? usuario = null)
        {
            var ctx = _http?.HttpContext;
            var d = detalle ?? "";
            return new Bitacora
            {
                Fecha = DateTime.Now,
                Usuario = Recortar(usuario ?? ctx?.User?.Identity?.Name ?? "sistema", 50),
                Accion = Recortar(accion, 60),
                Detalle = Recortar(d, 500),
                Ip = Recortar(ctx?.Connection.RemoteIpAddress?.ToString() ?? "", 60)
            };
        }

        private static string Recortar(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        private static string NombreTipo(object o) => o switch
        {
            Usuario => "usuario",
            Cliente => "cliente",
            Contratacion => "contratación",
            Evento => "evento",
            Pago => "pago",
            Gasto => "gasto",
            Integrante => "integrante",
            MovimientoIntegrante => "deuda",
            Documento => "documento",
            _ => o.GetType().Name.ToLowerInvariant()
        };

        private static readonly CultureInfo Mx = new("es-MX");
        private static string Describir(object o) => o switch
        {
            Usuario u => u.NombreUsuario,
            Cliente c => c.NombreCompleto,
            Contratacion c => $"#{c.Id}" + (c.Cliente != null ? $" de {c.Cliente.NombreCompleto}" : "") + $" ({c.PrecioAcordado.ToString("C0", Mx)})",
            Evento e => e.Inicio.ToString("d MMM yyyy h:mm tt", Mx),
            Pago p => $"{p.Monto.ToString("C0", Mx)} de la contratación #{p.ContratacionId}",
            Gasto g => $"{g.Categoria} {g.Monto.ToString("C0", Mx)}",
            Integrante i => i.NombreCompleto,
            MovimientoIntegrante m => $"{m.Monto.ToString("C0", Mx)}" + (m.Integrante != null ? $" con {m.Integrante.NombreCompleto}" : ""),
            Documento d => d.Folio,
            _ => o.GetType().Name
        };

        // ================================================================ Limpieza
        private void LiberarMemoria()
        {
            if (_nombreMemoria == null) return;
            try { Database.EnsureDeleted(); } catch { /* el contexto ya estaba cerrado */ }
            NombresLibres.Add(_nombreMemoria);
            _nombreMemoria = null;
        }

        public override void Dispose()
        {
            LiberarMemoria();
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            LiberarMemoria();
            return base.DisposeAsync();
        }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Usuario>(e =>
            {
                e.ToTable("Usuarios");
                e.HasIndex(x => x.NombreUsuario).IsUnique();
                e.HasOne(x => x.Integrante).WithMany().HasForeignKey(x => x.IntegranteId).OnDelete(DeleteBehavior.SetNull);
            });

            mb.Entity<Cliente>(e =>
            {
                e.ToTable("Clientes");
                e.HasIndex(x => x.Telefono);
                e.HasIndex(x => x.NombreCompleto);
            });

            mb.Entity<Servicio>(e =>
            {
                e.ToTable("Servicios");
                e.Property(x => x.PrecioBase).HasPrecision(12, 2);
                e.Property(x => x.DuracionHoras).HasPrecision(4, 1);
            });

            mb.Entity<Contratacion>(e =>
            {
                e.ToTable("Contrataciones", t =>
                    t.HasCheckConstraint("CK_Contrataciones_Precio", "[PrecioAcordado] >= 0"));
                e.Property(x => x.PrecioAcordado).HasPrecision(12, 2);
                e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
                e.HasIndex(x => x.Estado);

                e.HasOne(x => x.Cliente).WithMany(c => c.Contrataciones)
                    .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Servicio).WithMany(s => s.Contrataciones)
                    .HasForeignKey(x => x.ServicioId).OnDelete(DeleteBehavior.SetNull);
            });

            mb.Entity<Evento>(e =>
            {
                e.ToTable("Eventos", t =>
                    t.HasCheckConstraint("CK_Eventos_Horario", "[Fin] > [Inicio]"));
                e.HasIndex(x => x.ContratacionId).IsUnique();
                e.HasIndex(x => x.Inicio);
                e.HasOne(x => x.Contratacion).WithOne(c => c.Evento!)
                    .HasForeignKey<Evento>(x => x.ContratacionId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<Pago>(e =>
            {
                e.ToTable("Pagos", t =>
                    t.HasCheckConstraint("CK_Pagos_Monto", "[Monto] > 0"));
                e.Property(x => x.Monto).HasPrecision(12, 2);
                e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.Metodo).HasConversion<string>().HasMaxLength(20);
                e.HasIndex(x => x.Fecha);
                e.HasOne(x => x.Contratacion).WithMany(c => c.Pagos)
                    .HasForeignKey(x => x.ContratacionId).OnDelete(DeleteBehavior.Restrict);
            });

            mb.Entity<Gasto>(e =>
            {
                e.ToTable("Gastos", t =>
                    t.HasCheckConstraint("CK_Gastos_Monto", "[Monto] > 0"));
                e.Property(x => x.Monto).HasPrecision(12, 2);
                e.HasIndex(x => x.Fecha);
                e.HasOne(x => x.Contratacion).WithMany(c => c.Gastos)
                    .HasForeignKey(x => x.ContratacionId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(x => x.Integrante).WithMany()
                    .HasForeignKey(x => x.IntegranteId).OnDelete(DeleteBehavior.SetNull);
            });

            mb.Entity<Integrante>(e => e.ToTable("Integrantes"));

            mb.Entity<MovimientoIntegrante>(e =>
            {
                e.ToTable("MovimientosIntegrantes", t =>
                    t.HasCheckConstraint("CK_MovimientosIntegrantes_Monto", "[Monto] > 0"));
                e.Property(x => x.Monto).HasPrecision(12, 2);
                e.Property(x => x.Direccion).HasConversion<string>().HasMaxLength(10);
                e.HasOne(x => x.Integrante).WithMany(i => i.Movimientos)
                    .HasForeignKey(x => x.IntegranteId).OnDelete(DeleteBehavior.Cascade);
            });
            mb.Entity<Reunion>(e => { e.ToTable("Reuniones"); e.HasIndex(x => x.FechaHora); });
            mb.Entity<Cancion>(e => e.ToTable("Canciones"));

            mb.Entity<Documento>(e =>
            {
                e.ToTable("Documentos");
                e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
                e.HasIndex(x => x.Folio).IsUnique();
                e.HasOne(x => x.Contratacion).WithMany(c => c.Documentos)
                    .HasForeignKey(x => x.ContratacionId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Pago).WithMany()
                    .HasForeignKey(x => x.PagoId).OnDelete(DeleteBehavior.Restrict);
            });

            mb.Entity<Bitacora>(e =>
            {
                e.ToTable("Bitacora");
                e.HasIndex(x => x.Fecha);
            });

            // En MongoDB los Ids salen de un contador atómico (como IDENTITY en SQL Server)
            if (ModoMongo)
            {
                foreach (var tipo in mb.Model.GetEntityTypes().ToList())
                {
                    var clave = tipo.FindPrimaryKey()?.Properties.SingleOrDefault();
                    if (clave != null && clave.ClrType == typeof(int))
                        mb.Entity(tipo.ClrType).Property(clave.Name).ValueGeneratedOnAdd().HasValueGenerator<GeneradorIdMongo>();
                }
            }
        }
    }
}
