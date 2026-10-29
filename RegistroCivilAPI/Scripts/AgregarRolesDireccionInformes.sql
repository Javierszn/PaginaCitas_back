USE [RegistroCivil_Citas];
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE nombre_rol = N'Dirección')
    INSERT INTO dbo.Roles (nombre_rol) VALUES (N'Dirección');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE nombre_rol = N'Informes')
    INSERT INTO dbo.Roles (nombre_rol) VALUES (N'Informes');

COMMIT TRANSACTION;

SELECT id_rol, nombre_rol
FROM dbo.Roles
ORDER BY id_rol;
