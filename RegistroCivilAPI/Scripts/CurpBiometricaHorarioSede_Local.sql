/* Run in SSMS with RegistroCivil_Citas selected. */
IF DB_NAME() <> N'RegistroCivil_Citas'
    THROW 51000, 'Select the local RegistroCivil_Citas database before running this script.', 1;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    -- Execute schema changes in separate dynamic batches so SQL Server does not
    -- compile later references before these columns exist.
    IF COL_LENGTH(N'dbo.Tramites', N'id_sede') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Tramites ADD id_sede int NULL;';
    IF COL_LENGTH(N'dbo.Tramites', N'hora_apertura') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Tramites ADD hora_apertura time NULL;';
    IF COL_LENGTH(N'dbo.Tramites', N'hora_cierre') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Tramites ADD hora_cierre time NULL;';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tramites') AND name = N'IX_Tramites_id_sede')
        EXEC sys.sp_executesql N'CREATE INDEX IX_Tramites_id_sede ON dbo.Tramites(id_sede);';

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Tramites_Sedes_id_sede')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Tramites WITH CHECK ADD CONSTRAINT FK_Tramites_Sedes_id_sede FOREIGN KEY (id_sede) REFERENCES dbo.Sedes(id_sede);';

    EXEC sys.sp_executesql N'
DECLARE @CategoriaCurp int = (
    SELECT TOP (1) id_categoria FROM dbo.Categorias_Tramites
    WHERE id_categoria = 4 AND nombre_categoria LIKE N''%CURP%''
);
IF @CategoriaCurp IS NULL THROW 51002, ''No se encontró la categoría CURP (id_categoria 4).'', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.Tramites
    WHERE id_categoria = @CategoriaCurp AND nombre_tramite = N''CURP BIOMÉTRICA''
)
BEGIN
    INSERT dbo.Tramites
        (id_categoria, nombre_tramite, descripcion, duracion_minutos,
         limite_diario_sede, costo, activo, requisitos,
         fecha_inicio_permitida, fecha_fin_permitida,
         id_sede, hora_apertura, hora_cierre)
    VALUES
        (@CategoriaCurp, N''CURP BIOMÉTRICA'',
         N''Registro de datos biométricos (huellas dactilares, iris y rostro) para fortalecer la seguridad e identidad.'',
         15, 50, 0, 1,
         N''CURP impresa certificada por RENAPO, Identificación oficial vigente, Correo electrónico personal'',
         NULL, NULL, NULL, ''08:00'', ''15:00'');
END
ELSE
BEGIN
    UPDATE dbo.Tramites
    SET id_sede = NULL,
        activo = 1,
        hora_apertura = ''08:00'',
        hora_cierre = ''15:00'',
        requisitos = N''CURP impresa certificada por RENAPO, Identificación oficial vigente, Correo electrónico personal''
    WHERE id_categoria = @CategoriaCurp AND nombre_tramite = N''CURP BIOMÉTRICA'';
END;';

    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261104120000_CurpBiometricaHorarioSede')
        INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
        VALUES (N'20261104120000_CurpBiometricaHorarioSede', N'10.0.9');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

EXEC sys.sp_executesql N'
SELECT t.id_tramite, t.nombre_tramite, COALESCE(s.nombre, N''Todas las sedes'') AS sede,
       t.duracion_minutos, t.costo, t.limite_diario_sede,
       t.hora_apertura, t.hora_cierre, t.requisitos
FROM dbo.Tramites t
LEFT JOIN dbo.Sedes s ON s.id_sede = t.id_sede
WHERE t.nombre_tramite = N''CURP BIOMÉTRICA'';';
