/*
   Sincroniza las sedes de RegistroCivil_Citas con la información de la
   versión local de Citas y agrega/configura Rioverde.

   Modo seguro inicial: @Aplicar = 0 solo muestra el plan y revierte la
   transacción. Revisa los datos y cambia a 1 para aplicar.

   El respaldo antiguo puede tener la fila de Tamazunchale etiquetada con
   municipio Rioverde. Este script corrige esa fila primero y agrega Rioverde
   como sede separada, conservando sus citas y trámites.

   Supuesto operativo: Rioverde tendrá los mismos horarios y catálogo de
   trámites que la sede Dirección del Registro Civil - San Luis Potosí.
   El script conserva citas, trámites existentes y días inhábiles.
*/
USE [RegistroCivil_Citas];
GO

IF DB_NAME() <> N'RegistroCivil_Citas'
    THROW 51000, 'Selecciona la base RegistroCivil_Citas antes de ejecutar este script.', 1;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar bit = 0; -- Cambiar a 1 únicamente después de revisar la vista previa.
DECLARE @SedeRioverde int;
DECLARE @CoincidenciasRioverde int;
DECLARE @SedeTamazunchale int;
DECLARE @CoincidenciasTamazunchale int;
DECLARE @SedePlantilla int;
DECLARE @CoincidenciasPlantilla int;
DECLARE @CoincidenciasSLP int;
DECLARE @CoincidenciasCharcas int;
DECLARE @CoincidenciasValles int;
DECLARE @CoincidenciasOtrosEstados int;

SELECT @CoincidenciasSLP = COUNT(*) FROM dbo.Sedes WHERE nombre LIKE N'%Registro Civil%';
SELECT @CoincidenciasCharcas = COUNT(*) FROM dbo.Sedes WHERE nombre LIKE N'%Charcas%';
SELECT @CoincidenciasValles = COUNT(*) FROM dbo.Sedes WHERE nombre LIKE N'%Valles%';
SELECT @CoincidenciasOtrosEstados = COUNT(*) FROM dbo.Sedes WHERE nombre LIKE N'%Otros Estados%';

IF @CoincidenciasSLP <> 1 OR @CoincidenciasCharcas <> 1 OR
   @CoincidenciasValles <> 1 OR @CoincidenciasOtrosEstados <> 1
    THROW 51004, 'No se reconoció exactamente una sede de SLP, Charcas, Valles y Otros Estados. Revisa dbo.Sedes; no se modificó ninguna fila.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

/* Datos de ubicación y contacto mostrados por la versión local de Citas. */
UPDATE dbo.Sedes
SET municipio = N'San Luis Potosí',
    direccion = N'Simón Bolívar 965, Zona Centro, C.P. 78000, San Luis Potosí, S.L.P. | Tel. 444 100 9000',
    activa = 1
WHERE nombre LIKE N'%Registro Civil%';

UPDATE dbo.Sedes
SET nombre = N'Oficina Enlace Charcas - Altiplano',
    municipio = N'Charcas',
    direccion = N'Andador Francisco I. Madero 103, Zona Centro, Charcas, S.L.P. (Sin número telefónico)',
    activa = 1
WHERE nombre LIKE N'%Charcas%';

UPDATE dbo.Sedes
SET nombre = N'Oficina Enlace Valles - Zona Huasteca',
    municipio = N'Ciudad Valles',
    direccion = N'Calle Negrete 702, Zona Centro, Ciudad Valles, S.L.P. (Sin número telefónico)',
    activa = 1
WHERE nombre LIKE N'%Valles%';

UPDATE dbo.Sedes
SET nombre = N'Otros Estados',
    municipio = N'Nacional',
    direccion = N'Atención para ciudadanos de otras entidades',
    activa = 1
WHERE nombre LIKE N'%Otros Estados%';

/* Corrige el municipio/domicilio de Tamazunchale si el respaldo lo mezcló
   con Rioverde. No cambia el id de sede ni sus citas/trámites asociados. */
SELECT @CoincidenciasTamazunchale = COUNT(*),
       @SedeTamazunchale = MIN(id_sede)
FROM dbo.Sedes
WHERE nombre LIKE N'%Tamazunchale%';

IF @CoincidenciasTamazunchale > 1
    THROW 51003, 'Hay más de una sede llamada Tamazunchale. Revisa dbo.Sedes antes de continuar.', 1;

IF @SedeTamazunchale IS NOT NULL
BEGIN
    UPDATE dbo.Sedes
    SET municipio = N'Tamazunchale',
        direccion = N'Carretera Nacional México-Laredo S/N, Barrio La Cruz-Ixtlapalaco, Tamazunchale, S.L.P. (Sin número telefónico)',
        activa = 1
    WHERE id_sede = @SedeTamazunchale;
END;

SELECT @CoincidenciasRioverde = COUNT(*),
       @SedeRioverde = MIN(id_sede)
FROM dbo.Sedes
WHERE municipio = N'Rioverde' OR nombre LIKE N'%Rioverde%';

IF @CoincidenciasRioverde > 1
    THROW 51001, 'Hay más de una sede relacionada con Rioverde. Revisa dbo.Sedes antes de continuar; no se modificó ninguna fila.', 1;

SELECT @CoincidenciasPlantilla = COUNT(*),
       @SedePlantilla = MIN(id_sede)
FROM dbo.Sedes
WHERE municipio = N'San Luis Potosí'
  AND nombre LIKE N'%Registro Civil%';

IF @CoincidenciasPlantilla <> 1
    THROW 51002, 'No se identificó una única sede plantilla de San Luis Potosí. Revisa dbo.Sedes antes de continuar.', 1;

    IF @SedeRioverde IS NULL
    BEGIN
        INSERT dbo.Sedes (nombre, municipio, direccion, activa)
        VALUES (N'Oficina Enlace Rioverde - Zona Sur', N'Rioverde',
                N'Oficialía de Enlace Rioverde | Tel: sin teléfono', 1);

        SET @SedeRioverde = CONVERT(int, SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Sedes
        SET nombre = N'Oficina Enlace Rioverde - Zona Sur',
            municipio = N'Rioverde',
            direccion = N'Oficialía de Enlace Rioverde | Tel: sin teléfono',
            activa = 1
        WHERE id_sede = @SedeRioverde;
    END;

    /* Copia el horario de la sede plantilla solo para días faltantes. */
    INSERT dbo.Horarios_Sede (id_sede, dia_semana, hora_apertura, hora_cierre)
    SELECT @SedeRioverde, h.dia_semana, h.hora_apertura, h.hora_cierre
    FROM dbo.Horarios_Sede AS h
    WHERE h.id_sede = @SedePlantilla
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.Horarios_Sede AS actual
          WHERE actual.id_sede = @SedeRioverde
            AND actual.dia_semana = h.dia_semana
      );

    /*
       Copia la configuración por sede y la configuración global. Si existe
       el mismo trámite en ambas fuentes, prefiere la configuración específica
       de la sede plantilla. No inserta trámites repetidos en Rioverde.
    */
    ;WITH CatalogoFuente AS
    (
        SELECT t.id_categoria, t.nombre_tramite, t.descripcion,
               t.duracion_minutos, t.limite_diario_sede, t.costo,
               t.activo, t.requisitos, t.fecha_inicio_permitida,
               t.fecha_fin_permitida, t.hora_apertura, t.hora_cierre,
               ROW_NUMBER() OVER (
                   PARTITION BY t.id_categoria, t.nombre_tramite
                   ORDER BY CASE WHEN t.id_sede = @SedePlantilla THEN 0 ELSE 1 END,
                            t.id_tramite
               ) AS orden
        FROM dbo.Tramites AS t
        WHERE t.id_sede = @SedePlantilla OR t.id_sede IS NULL
    )
    INSERT dbo.Tramites
        (id_categoria, nombre_tramite, descripcion, duracion_minutos,
         limite_diario_sede, costo, activo, requisitos,
         fecha_inicio_permitida, fecha_fin_permitida, id_sede,
         hora_apertura, hora_cierre)
    SELECT fuente.id_categoria, fuente.nombre_tramite, fuente.descripcion,
           fuente.duracion_minutos, fuente.limite_diario_sede, fuente.costo,
           fuente.activo, fuente.requisitos, fuente.fecha_inicio_permitida,
           fuente.fecha_fin_permitida, @SedeRioverde,
           fuente.hora_apertura, fuente.hora_cierre
    FROM CatalogoFuente AS fuente
    WHERE fuente.orden = 1
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.Tramites AS actual
          WHERE actual.id_sede = @SedeRioverde
            AND actual.id_categoria = fuente.id_categoria
            AND actual.nombre_tramite = fuente.nombre_tramite
      );

    SELECT @SedeRioverde AS id_sede, nombre, municipio, direccion, activa
    FROM dbo.Sedes
    WHERE id_sede IN (@SedeRioverde, @SedeTamazunchale);

    SELECT dia_semana, hora_apertura, hora_cierre
    FROM dbo.Horarios_Sede
    WHERE id_sede = @SedeRioverde
    ORDER BY dia_semana;

    SELECT id_categoria, nombre_tramite, activo, duracion_minutos,
           limite_diario_sede, costo, hora_apertura, hora_cierre
    FROM dbo.Tramites
    WHERE id_sede = @SedeRioverde
    ORDER BY id_categoria, nombre_tramite;

    IF @Aplicar = 1
    BEGIN
        COMMIT TRANSACTION;
        PRINT N'Cambios aplicados para la sede Rioverde.';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'Vista previa: no se guardaron cambios. Cambia @Aplicar a 1 para aplicar.';
    END;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
