/* =====================================================================
   ELEGIDO MANAGER · REINICIAR DATOS
   Deja la aplicación en blanco para empezar a usarla en serio.

   SE BORRA:  contrataciones y agenda (eventos), pagos, contratos PDF,
              clientes, gastos/finanzas, deudas de integrantes y
              TODOS los usuarios excepto "alain".
   SE QUEDA:  la cuenta "alain" (Administrador) y los integrantes
              (Omar, Andy, Ángel, Fernando).

   ¡No se puede deshacer! Ejecuta esto con la base ElegidoManagerDB.
   ===================================================================== */
USE ElegidoManagerDB;
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Usuarios WHERE NombreUsuario = N'alain')
BEGIN
    RAISERROR(N'No existe el usuario "alain". No se borró nada.', 16, 1);
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM dbo.Documentos;
    DELETE FROM dbo.Gastos;
    DELETE FROM dbo.Pagos;
    DELETE FROM dbo.Eventos;
    DELETE FROM dbo.Contrataciones;
    DELETE FROM dbo.Clientes;
    DELETE FROM dbo.MovimientosIntegrantes;
    DELETE FROM dbo.Usuarios WHERE NombreUsuario <> N'alain';

    -- Asegura que alain siga como administrador activo
    UPDATE dbo.Usuarios SET Rol = N'Administrador', Activo = 1, IntegranteId = NULL WHERE NombreUsuario = N'alain';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @msg NVARCHAR(4000) = N'No se borró nada. Error: ' + ERROR_MESSAGE();
    RAISERROR(@msg, 16, 1);
    RETURN;
END CATCH

SELECT N'Listo: datos reiniciados.' AS Resultado,
       (SELECT COUNT(*) FROM dbo.Usuarios) AS Usuarios,
       (SELECT COUNT(*) FROM dbo.Integrantes) AS Integrantes,
       (SELECT COUNT(*) FROM dbo.Contrataciones) AS Contrataciones;
