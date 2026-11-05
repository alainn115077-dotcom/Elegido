/* =====================================================================
   ELEGIDO MANAGER - Grupo Elegido Sierreño
   Script de creación de la base de datos (SQL Server)

   NOTA: La aplicación crea esta misma estructura automáticamente al
   arrancar por primera vez (EnsureCreated). Usa este script solo si
   prefieres crear la base manualmente desde SSMS.
   No contiene datos de demostración.
   ===================================================================== */

IF DB_ID(N'ElegidoManagerDB') IS NULL
    CREATE DATABASE ElegidoManagerDB;
GO
USE ElegidoManagerDB;
GO

/* ---------- Usuarios del sistema ---------- */
CREATE TABLE Usuarios (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Usuarios PRIMARY KEY,
    NombreUsuario   NVARCHAR(50)  NOT NULL,
    NombreCompleto  NVARCHAR(100) NOT NULL,
    PasswordHash    NVARCHAR(200) NOT NULL,
    Rol             NVARCHAR(20)  NOT NULL,
    Activo          BIT           NOT NULL,
    FechaCreacion   DATETIME2     NOT NULL,
    UltimoAcceso    DATETIME2     NULL
);
CREATE UNIQUE INDEX IX_Usuarios_NombreUsuario ON Usuarios(NombreUsuario);
GO

/* ---------- Clientes ---------- */
CREATE TABLE Clientes (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Clientes PRIMARY KEY,
    NombreCompleto  NVARCHAR(120) NOT NULL,
    Telefono        NVARCHAR(20)  NOT NULL,
    Ciudad          NVARCHAR(80)  NULL,
    Correo          NVARCHAR(120) NULL,
    Notas           NVARCHAR(500) NULL,
    FechaRegistro   DATETIME2     NOT NULL
);
CREATE INDEX IX_Clientes_Telefono ON Clientes(Telefono);
CREATE INDEX IX_Clientes_NombreCompleto ON Clientes(NombreCompleto);
GO

/* ---------- Servicios / paquetes que ofrece el grupo ---------- */
CREATE TABLE Servicios (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Servicios PRIMARY KEY,
    Nombre          NVARCHAR(100) NOT NULL,
    Descripcion     NVARCHAR(300) NULL,
    DuracionHoras   DECIMAL(4,1)  NULL,
    PrecioBase      DECIMAL(12,2) NULL,
    Activo          BIT           NOT NULL
);
GO

/* ---------- Contrataciones ---------- */
CREATE TABLE Contrataciones (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Contrataciones PRIMARY KEY,
    ClienteId           INT            NOT NULL,
    ServicioId          INT            NULL,
    TipoEvento          NVARCHAR(50)   NOT NULL,
    Estado              NVARCHAR(20)   NOT NULL,   -- Pendiente | Confirmada | Cancelada
    PrecioAcordado      DECIMAL(12,2)  NOT NULL,
    Observaciones       NVARCHAR(1000) NULL,
    MotivoCancelacion   NVARCHAR(300)  NULL,
    FechaRegistro       DATETIME2      NOT NULL,
    FechaActualizacion  DATETIME2      NULL,
    CONSTRAINT CK_Contrataciones_Precio CHECK ([PrecioAcordado] >= 0),
    CONSTRAINT FK_Contrataciones_Clientes_ClienteId FOREIGN KEY (ClienteId) REFERENCES Clientes(Id),
    CONSTRAINT FK_Contrataciones_Servicios_ServicioId FOREIGN KEY (ServicioId) REFERENCES Servicios(Id) ON DELETE SET NULL
);
CREATE INDEX IX_Contrataciones_ClienteId ON Contrataciones(ClienteId);
CREATE INDEX IX_Contrataciones_ServicioId ON Contrataciones(ServicioId);
CREATE INDEX IX_Contrataciones_Estado ON Contrataciones(Estado);
GO

/* ---------- Eventos (agenda y logística, 1 a 1 con Contrataciones) ---------- */
CREATE TABLE Eventos (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Eventos PRIMARY KEY,
    ContratacionId  INT           NOT NULL,
    Inicio          DATETIME2     NOT NULL,
    Fin             DATETIME2     NOT NULL,
    Lugar           NVARCHAR(120) NULL,
    Direccion       NVARCHAR(250) NOT NULL,
    Ciudad          NVARCHAR(80)  NOT NULL,
    PuntoReunion    NVARCHAR(150) NULL,
    HoraReunion     TIME          NULL,
    HoraSalida      TIME          NULL,
    NotasLogistica  NVARCHAR(500) NULL,
    CONSTRAINT CK_Eventos_Horario CHECK ([Fin] > [Inicio]),
    CONSTRAINT FK_Eventos_Contrataciones_ContratacionId FOREIGN KEY (ContratacionId) REFERENCES Contrataciones(Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IX_Eventos_ContratacionId ON Eventos(ContratacionId);
CREATE INDEX IX_Eventos_Inicio ON Eventos(Inicio);
GO

/* ---------- Pagos (anticipos, abonos y liquidaciones) ---------- */
CREATE TABLE Pagos (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pagos PRIMARY KEY,
    ContratacionId  INT           NOT NULL,
    Tipo            NVARCHAR(20)  NOT NULL,    -- Anticipo | Abono | Liquidacion
    Metodo          NVARCHAR(20)  NOT NULL,    -- Efectivo | Transferencia | Deposito | Otro
    Monto           DECIMAL(12,2) NOT NULL,
    Fecha           DATETIME2     NOT NULL,
    Referencia      NVARCHAR(100) NULL,
    Notas           NVARCHAR(300) NULL,
    FechaRegistro   DATETIME2     NOT NULL,
    CONSTRAINT CK_Pagos_Monto CHECK ([Monto] > 0),
    CONSTRAINT FK_Pagos_Contrataciones_ContratacionId FOREIGN KEY (ContratacionId) REFERENCES Contrataciones(Id)
);
CREATE INDEX IX_Pagos_ContratacionId ON Pagos(ContratacionId);
CREATE INDEX IX_Pagos_Fecha ON Pagos(Fecha);
GO

/* ---------- Gastos ---------- */
CREATE TABLE Gastos (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Gastos PRIMARY KEY,
    Concepto        NVARCHAR(150) NOT NULL,
    Categoria       NVARCHAR(50)  NOT NULL,
    Monto           DECIMAL(12,2) NOT NULL,
    Fecha           DATETIME2     NOT NULL,
    ContratacionId  INT           NULL,
    Notas           NVARCHAR(300) NULL,
    CONSTRAINT CK_Gastos_Monto CHECK ([Monto] > 0),
    CONSTRAINT FK_Gastos_Contrataciones_ContratacionId FOREIGN KEY (ContratacionId) REFERENCES Contrataciones(Id) ON DELETE SET NULL
);
CREATE INDEX IX_Gastos_ContratacionId ON Gastos(ContratacionId);
CREATE INDEX IX_Gastos_Fecha ON Gastos(Fecha);
GO

/* ---------- Integrantes del grupo ---------- */
CREATE TABLE Integrantes (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Integrantes PRIMARY KEY,
    NombreCompleto  NVARCHAR(120) NOT NULL,
    Instrumento     NVARCHAR(80)  NOT NULL,
    Telefono        NVARCHAR(20)  NULL,
    FechaIngreso    DATETIME2     NULL,
    Activo          BIT           NOT NULL,
    Notas           NVARCHAR(300) NULL
);
GO

/* ---------- Ensayos y reuniones ---------- */
CREATE TABLE Reuniones (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reuniones PRIMARY KEY,
    Titulo      NVARCHAR(120) NOT NULL,
    Tipo        NVARCHAR(30)  NOT NULL,
    FechaHora   DATETIME2     NOT NULL,
    Lugar       NVARCHAR(150) NULL,
    Notas       NVARCHAR(500) NULL
);
CREATE INDEX IX_Reuniones_FechaHora ON Reuniones(FechaHora);
GO

/* ---------- Repertorio musical ---------- */
CREATE TABLE Canciones (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Canciones PRIMARY KEY,
    Titulo      NVARCHAR(150) NOT NULL,
    Artista     NVARCHAR(120) NULL,
    Genero      NVARCHAR(60)  NULL,
    Tonalidad   NVARCHAR(20)  NULL,
    Notas       NVARCHAR(300) NULL,
    Activa      BIT           NOT NULL
);
GO

/* ---------- Documentos PDF generados ---------- */
CREATE TABLE Documentos (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Documentos PRIMARY KEY,
    ContratacionId   INT            NOT NULL,
    PagoId           INT            NULL,
    Tipo             NVARCHAR(30)   NOT NULL,  -- Contrato | ComprobantePago | ReciboLiquidacion
    Folio            NVARCHAR(30)   NOT NULL,
    FechaGeneracion  DATETIME2      NOT NULL,
    GeneradoPor      NVARCHAR(100)  NOT NULL,
    NombreArchivo    NVARCHAR(150)  NOT NULL,
    Contenido        VARBINARY(MAX) NOT NULL,
    CONSTRAINT FK_Documentos_Contrataciones_ContratacionId FOREIGN KEY (ContratacionId) REFERENCES Contrataciones(Id),
    CONSTRAINT FK_Documentos_Pagos_PagoId FOREIGN KEY (PagoId) REFERENCES Pagos(Id)
);
CREATE UNIQUE INDEX IX_Documentos_Folio ON Documentos(Folio);
CREATE INDEX IX_Documentos_ContratacionId ON Documentos(ContratacionId);
CREATE INDEX IX_Documentos_PagoId ON Documentos(PagoId);
GO

/* ---------- Vista útil: saldo de cada contratación (calculado, no almacenado) ---------- */
CREATE VIEW vw_SaldosContrataciones AS
SELECT  c.Id AS ContratacionId,
        cl.NombreCompleto AS Cliente,
        c.TipoEvento,
        c.Estado,
        e.Inicio,
        c.PrecioAcordado,
        ISNULL((SELECT SUM(p.Monto) FROM Pagos p WHERE p.ContratacionId = c.Id), 0) AS TotalPagado,
        c.PrecioAcordado - ISNULL((SELECT SUM(p.Monto) FROM Pagos p WHERE p.ContratacionId = c.Id), 0) AS SaldoPendiente
FROM Contrataciones c
JOIN Clientes cl ON cl.Id = c.ClienteId
LEFT JOIN Eventos e ON e.ContratacionId = c.Id;
GO

/* ---------- Deudas con integrantes (me deben / les debo) ---------- */
CREATE TABLE MovimientosIntegrantes (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MovimientosIntegrantes PRIMARY KEY,
    IntegranteId    INT            NOT NULL,
    Direccion       NVARCHAR(10)   NOT NULL,   -- MeDebe | LeDebo
    Monto           DECIMAL(12,2)  NOT NULL,
    Concepto        NVARCHAR(150)  NULL,
    Fecha           DATETIME2      NOT NULL,
    Liquidado       BIT            NOT NULL,
    FechaLiquidado  DATETIME2      NULL,
    FechaRegistro   DATETIME2      NOT NULL,
    CONSTRAINT CK_MovimientosIntegrantes_Monto CHECK ([Monto] > 0),
    CONSTRAINT FK_MovimientosIntegrantes_Integrantes_IntegranteId FOREIGN KEY (IntegranteId) REFERENCES Integrantes(Id) ON DELETE CASCADE
);
CREATE INDEX IX_MovimientosIntegrantes_IntegranteId ON MovimientosIntegrantes(IntegranteId);
GO
INSERT INTO Integrantes (NombreCompleto, Instrumento, Activo) VALUES (N'Omar', N'', 1), (N'Andy', N'', 1), (N'Ángel', N'', 1), (N'Fernando', N'', 1);
GO

/* ---------- Pagos a integrantes por evento (se guardan como gastos) ---------- */
ALTER TABLE Gastos ADD IntegranteId INT NULL;
GO
ALTER TABLE Gastos ADD CONSTRAINT FK_Gastos_Integrantes_IntegranteId FOREIGN KEY (IntegranteId) REFERENCES Integrantes(Id) ON DELETE SET NULL;
CREATE INDEX IX_Gastos_IntegranteId ON Gastos(IntegranteId);
GO

/* ---------- Horario pendiente en eventos ---------- */
ALTER TABLE Eventos ADD HorarioPendiente BIT NOT NULL CONSTRAINT DF_Eventos_HorarioPendiente DEFAULT 0;
GO

-- Cuenta de usuario ligada a un integrante (rol Integrante: ve sus deudas)
IF COL_LENGTH(N'dbo.Usuarios', N'IntegranteId') IS NULL ALTER TABLE dbo.Usuarios ADD IntegranteId INT NULL;
GO
IF OBJECT_ID(N'dbo.FK_Usuarios_Integrantes_IntegranteId', N'F') IS NULL
    ALTER TABLE dbo.Usuarios ADD CONSTRAINT FK_Usuarios_Integrantes_IntegranteId FOREIGN KEY (IntegranteId) REFERENCES dbo.Integrantes(Id) ON DELETE SET NULL;
GO
