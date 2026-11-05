using ElegidoManager.Models;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

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
        }
    }
}
