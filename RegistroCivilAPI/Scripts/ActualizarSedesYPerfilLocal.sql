/*
   Ejecutar solo en la instancia SQL Server LOCAL RegistroCivil_Citas.
   Agrega correo/teléfono a cuentas internas, actualiza sedes y corrige los
   trámites duplicados de Rioverde. Antes de borrar una fila duplicada,
   reasigna sus citas a la fila conservada.
*/
USE [RegistroCivil_Citas];
GO

IF DB_NAME() <> N'RegistroCivil_Citas'
    THROW 51000, 'Selecciona la base local RegistroCivil_Citas antes de ejecutar este script.', 1;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Usuarios_Internos', 'correo') IS NULL
    ALTER TABLE dbo.Usuarios_Internos ADD correo varchar(100) NULL;

IF COL_LENGTH('dbo.Usuarios_Internos', 'telefono') IS NULL
    ALTER TABLE dbo.Usuarios_Internos ADD telefono varchar(20) NULL;

UPDATE dbo.Sedes
SET municipio = N'San Luis Potosí',
    direccion = N'Simón Bolívar 965, Zona Centro, C.P. 78000, San Luis Potosí, S.L.P. | Tel. 444 100 9000'
WHERE nombre LIKE '%Registro Civil%';

UPDATE dbo.Sedes
SET municipio = N'Charcas',
    direccion = N'Andador Francisco I. Madero 103, Zona Centro, Charcas, S.L.P. (Sin número telefónico)'
WHERE nombre LIKE '%Charcas%';

UPDATE dbo.Sedes
SET municipio = N'Ciudad Valles',
    direccion = N'Calle Negrete 702, Zona Centro, Ciudad Valles, S.L.P. (Sin número telefónico)'
WHERE nombre LIKE '%Valles%';

UPDATE dbo.Sedes
SET nombre = N'Oficina Enlace Tamazunchale - Huasteca Sur',
    municipio = N'Tamazunchale',
    direccion = N'Carretera Nacional México-Laredo S/N, Barrio La Cruz-Ixtlapalaco, Tamazunchale, S.L.P. (Sin número telefónico)'
WHERE nombre LIKE '%Tamazunchale%';

IF EXISTS (SELECT 1 FROM dbo.Sedes WHERE municipio = N'Rioverde' OR nombre LIKE '%Rioverde%')
BEGIN
    UPDATE dbo.Sedes
    SET nombre = N'Oficina Enlace Rioverde - Zona Sur',
        municipio = N'Rioverde',
        direccion = N'Oficialía de Enlace Rioverde | Tel: sin teléfono',
        activa = 1
    WHERE municipio = N'Rioverde' OR nombre LIKE '%Rioverde%';
END
ELSE
BEGIN
    INSERT INTO dbo.Sedes (nombre, municipio, direccion, activa)
    VALUES (N'Oficina Enlace Rioverde - Zona Sur', N'Rioverde', N'Oficialía de Enlace Rioverde | Tel: sin teléfono', 1);
END;

/* Rioverde se agregó después de materializar los trámites por sede.
   Copia su catálogo completo de la Dirección SLP, igual que las demás sedes,
   sin duplicar categorías/trámites si este script se vuelve a ejecutar. */
DECLARE @SedeRioverde int = (
    SELECT TOP (1) id_sede FROM dbo.Sedes
    WHERE nombre = N'Oficina Enlace Rioverde - Zona Sur'
    ORDER BY id_sede
);
DECLARE @SedePlantilla int = (
    SELECT TOP (1) id_sede FROM dbo.Sedes
    WHERE municipio = N'San Luis Potosí' OR nombre LIKE N'%Registro Civil%'
    ORDER BY id_sede
);

IF @SedeRioverde IS NOT NULL AND @SedePlantilla IS NOT NULL
BEGIN
    /* El respaldo del 06/11 trae dos filas por cada trámite de Rioverde.
       Conserva el id más alto (la versión insertada después, con la vigencia
       más reciente), mueve sus citas y elimina las copias anteriores. */
    ;WITH Duplicados AS
    (
        SELECT id_tramite,
               FIRST_VALUE(id_tramite) OVER (
                   PARTITION BY id_sede, id_categoria, nombre_tramite
                   ORDER BY id_tramite DESC
               ) AS id_conservar,
               ROW_NUMBER() OVER (
                   PARTITION BY id_sede, id_categoria, nombre_tramite
                   ORDER BY id_tramite DESC
               ) AS numero_fila
        FROM dbo.Tramites
        WHERE id_sede = @SedeRioverde
    )
    UPDATE cita
    SET id_tramite = duplicado.id_conservar
    FROM dbo.Citas AS cita
    INNER JOIN Duplicados AS duplicado ON duplicado.id_tramite = cita.id_tramite
    WHERE duplicado.numero_fila > 1;

    ;WITH Duplicados AS
    (
        SELECT id_tramite,
               ROW_NUMBER() OVER (
                   PARTITION BY id_sede, id_categoria, nombre_tramite
                   ORDER BY id_tramite DESC
               ) AS numero_fila
        FROM dbo.Tramites
        WHERE id_sede = @SedeRioverde
    )
    DELETE tramite
    FROM dbo.Tramites AS tramite
    INNER JOIN Duplicados AS duplicado ON duplicado.id_tramite = tramite.id_tramite
    WHERE duplicado.numero_fila > 1;

    INSERT dbo.Tramites
        (id_categoria, nombre_tramite, descripcion, duracion_minutos,
         limite_diario_sede, costo, activo, requisitos, fecha_inicio_permitida,
         fecha_fin_permitida, id_sede, hora_apertura, hora_cierre)
    SELECT plantilla.id_categoria, plantilla.nombre_tramite, plantilla.descripcion,
           plantilla.duracion_minutos, plantilla.limite_diario_sede, plantilla.costo,
           plantilla.activo, plantilla.requisitos, plantilla.fecha_inicio_permitida,
           plantilla.fecha_fin_permitida, @SedeRioverde,
           plantilla.hora_apertura, plantilla.hora_cierre
    FROM dbo.Tramites AS plantilla
    WHERE (plantilla.id_sede = @SedePlantilla OR plantilla.id_sede IS NULL)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.Tramites AS existente
          WHERE existente.id_sede = @SedeRioverde
            AND existente.id_categoria = plantilla.id_categoria
            AND existente.nombre_tramite = plantilla.nombre_tramite
      );
END;

/* Impide que una actualización futura vuelva a crear el mismo trámite
   dentro de una sede. Los trámites globales (id_sede NULL) quedan fuera. */
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Tramites')
      AND name = N'UX_Tramites_Sede_Categoria_Nombre'
)
    CREATE UNIQUE INDEX UX_Tramites_Sede_Categoria_Nombre
        ON dbo.Tramites (id_sede, id_categoria, nombre_tramite)
        WHERE id_sede IS NOT NULL;

COMMIT TRANSACTION;
GO

SELECT id_sede, nombre, municipio, direccion, activa
FROM dbo.Sedes
WHERE nombre LIKE '%Registro Civil%'
   OR nombre LIKE '%Charcas%'
   OR nombre LIKE '%Valles%'
   OR nombre LIKE '%Tamazunchale%'
   OR nombre LIKE '%Rioverde%'
ORDER BY id_sede;
GO

SELECT t.id_tramite, t.id_categoria, t.nombre_tramite, t.activo,
       t.hora_apertura, t.hora_cierre
FROM dbo.Tramites AS t
INNER JOIN dbo.Sedes AS s ON s.id_sede = t.id_sede
WHERE s.nombre = N'Oficina Enlace Rioverde - Zona Sur'
ORDER BY t.id_categoria, t.id_tramite;
GO
