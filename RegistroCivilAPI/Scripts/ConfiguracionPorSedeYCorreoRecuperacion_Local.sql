/* Ejecutar en la base LOCAL RegistroCivil_Citas.
   Conserva los trámites actuales y crea una copia configurable por cada sede.
   Los días inhábiles siguen siendo globales; no se alteran aquí. */
IF DB_NAME() <> N'RegistroCivil_Citas'
    THROW 51010, 'Seguridad: selecciona la base local RegistroCivil_Citas antes de ejecutar.', 1;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Peticiones_Soporte', N'correo_contacto') IS NULL
    ALTER TABLE dbo.Peticiones_Soporte ADD correo_contacto varchar(320) NULL;

/* Materializa para cada sede las filas actualmente globales. La API pública
   usará solo las filas de la sede solicitada, evitando duplicar opciones. */
INSERT dbo.Tramites
    (id_categoria, nombre_tramite, descripcion, duracion_minutos,
     limite_diario_sede, costo, activo, requisitos, fecha_inicio_permitida,
     fecha_fin_permitida, id_sede, hora_apertura, hora_cierre)
SELECT t.id_categoria, t.nombre_tramite, t.descripcion, t.duracion_minutos,
       t.limite_diario_sede, t.costo, t.activo, t.requisitos,
       t.fecha_inicio_permitida, t.fecha_fin_permitida, s.id_sede,
       t.hora_apertura, t.hora_cierre
FROM dbo.Tramites t
CROSS JOIN dbo.Sedes s
WHERE t.id_sede IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Tramites existente
      WHERE existente.id_sede = s.id_sede
        AND existente.id_categoria = t.id_categoria
        AND existente.nombre_tramite = t.nombre_tramite
  );

COMMIT TRANSACTION;
GO

SELECT DB_NAME() AS base_actual,
       (SELECT COUNT(*) FROM dbo.Tramites WHERE id_sede IS NOT NULL) AS tramites_configurados_por_sede,
       (SELECT COUNT(*) FROM dbo.Tramites WHERE id_sede IS NULL) AS tramites_globales_conservados,
       COL_LENGTH(N'dbo.Peticiones_Soporte', N'correo_contacto') AS columna_correo_contacto;
