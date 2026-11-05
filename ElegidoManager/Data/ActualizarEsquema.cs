using ElegidoManager.Models;
using Microsoft.EntityFrameworkCore;

namespace ElegidoManager.Data
{
    /// <summary>
    /// Agrega a una base de datos YA EXISTENTE las tablas nuevas que EnsureCreated no crea,
    /// sin tocar los datos que ya están guardados.
    /// </summary>
    public static class ActualizarEsquema
    {
        public static void Aplicar(AppDbContext db)
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.MovimientosIntegrantes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MovimientosIntegrantes (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MovimientosIntegrantes PRIMARY KEY,
        IntegranteId    INT            NOT NULL,
        Direccion       NVARCHAR(10)   NOT NULL,
        Monto           DECIMAL(12,2)  NOT NULL,
        Concepto        NVARCHAR(150)  NULL,
        Fecha           DATETIME2      NOT NULL,
        Liquidado       BIT            NOT NULL,
        FechaLiquidado  DATETIME2      NULL,
        FechaRegistro   DATETIME2      NOT NULL,
        CONSTRAINT CK_MovimientosIntegrantes_Monto CHECK ([Monto] > 0),
        CONSTRAINT FK_MovimientosIntegrantes_Integrantes_IntegranteId FOREIGN KEY (IntegranteId) REFERENCES dbo.Integrantes(Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_MovimientosIntegrantes_IntegranteId ON dbo.MovimientosIntegrantes(IntegranteId);
END");

            // Usuarios: cuenta ligada a un integrante del grupo (para que vea sus deudas)
            db.Database.ExecuteSqlRaw(
                "IF COL_LENGTH(N'dbo.Usuarios', N'IntegranteId') IS NULL ALTER TABLE dbo.Usuarios ADD IntegranteId INT NULL;");
            db.Database.ExecuteSqlRaw(
                "IF OBJECT_ID(N'dbo.FK_Usuarios_Integrantes_IntegranteId', N'F') IS NULL " +
                "ALTER TABLE dbo.Usuarios ADD CONSTRAINT FK_Usuarios_Integrantes_IntegranteId FOREIGN KEY (IntegranteId) REFERENCES dbo.Integrantes(Id) ON DELETE SET NULL;");

            // Gastos: columna para ligar el pago a un integrante (cada paso en su propio lote)
            db.Database.ExecuteSqlRaw(
                "IF COL_LENGTH(N'dbo.Gastos', N'IntegranteId') IS NULL ALTER TABLE dbo.Gastos ADD IntegranteId INT NULL;");
            db.Database.ExecuteSqlRaw(
                "IF OBJECT_ID(N'dbo.FK_Gastos_Integrantes_IntegranteId', N'F') IS NULL " +
                "ALTER TABLE dbo.Gastos ADD CONSTRAINT FK_Gastos_Integrantes_IntegranteId FOREIGN KEY (IntegranteId) REFERENCES dbo.Integrantes(Id) ON DELETE SET NULL;");
            db.Database.ExecuteSqlRaw(
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Gastos_IntegranteId' AND object_id = OBJECT_ID(N'dbo.Gastos')) " +
                "CREATE INDEX IX_Gastos_IntegranteId ON dbo.Gastos(IntegranteId);");

            // Eventos: horario pendiente
            db.Database.ExecuteSqlRaw(
                "IF COL_LENGTH(N'dbo.Eventos', N'HorarioPendiente') IS NULL " +
                "ALTER TABLE dbo.Eventos ADD HorarioPendiente BIT NOT NULL CONSTRAINT DF_Eventos_HorarioPendiente DEFAULT 0;");

            // Ya no se usa el estado "Pendiente": al registrar con anticipo la contratación queda confirmada
            db.Database.ExecuteSqlRaw("UPDATE dbo.Contrataciones SET Estado = N'Confirmada' WHERE Estado = N'Pendiente';");

            // Todos los anticipos se reciben por transferencia
            db.Database.ExecuteSqlRaw("UPDATE dbo.Pagos SET Metodo = N'Transferencia' WHERE Tipo = N'Anticipo' AND Metodo <> N'Transferencia';");

            // Teléfonos con el mismo formato (899 494 7922)
            foreach (var c in db.Clientes.ToList())
            {
                var t = ElegidoManager.Services.Formato.Telefono(c.Telefono);
                if (t != c.Telefono) c.Telefono = t;
            }
            foreach (var i in db.Integrantes.Where(x => x.Telefono != null).ToList())
            {
                var t = ElegidoManager.Services.Formato.Telefono(i.Telefono);
                if (t != i.Telefono) i.Telefono = t;
            }
            db.SaveChanges();

            // Integrantes del grupo (solo la primera vez, si la tabla está vacía)
            if (!db.Integrantes.Any())
            {
                foreach (var nombre in new[] { "Omar", "Andy", "Ángel", "Fernando" })
                    db.Integrantes.Add(new Integrante { NombreCompleto = nombre, Instrumento = "", Activo = true });
                db.SaveChanges();
            }

            // Reinicio único para empezar en limpio: se ejecuta UNA sola vez y queda marcado.
            // Borra agenda, pagos, contratos, clientes, gastos, deudas y usuarios (excepto "alain").
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.ReiniciosAplicados', N'U') IS NULL
    CREATE TABLE dbo.ReiniciosAplicados (Clave NVARCHAR(50) NOT NULL PRIMARY KEY, Fecha DATETIME2 NOT NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.ReiniciosAplicados WHERE Clave = N'inicio-limpio-2026-11')
BEGIN
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.Usuarios WHERE NombreUsuario = N'alain')
    BEGIN
        DELETE FROM dbo.Documentos;
        DELETE FROM dbo.Gastos;
        DELETE FROM dbo.Pagos;
        DELETE FROM dbo.Eventos;
        DELETE FROM dbo.Contrataciones;
        DELETE FROM dbo.Clientes;
        DELETE FROM dbo.MovimientosIntegrantes;
        DELETE FROM dbo.Usuarios WHERE NombreUsuario <> N'alain';
        UPDATE dbo.Usuarios SET Rol = N'Administrador', Activo = 1, IntegranteId = NULL WHERE NombreUsuario = N'alain';
    END
    INSERT INTO dbo.ReiniciosAplicados (Clave, Fecha) VALUES (N'inicio-limpio-2026-11', SYSDATETIME());
    COMMIT TRANSACTION;
END");
        }
    }
}
