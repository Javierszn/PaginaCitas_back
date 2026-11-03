-- Ejecutar una sola vez en la base RegistroCivil_Citas.
-- Permite guardar el estatus NO_CONFIRMADA en Citas.
IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CHK_EstatusCitas'
      AND parent_object_id = OBJECT_ID(N'dbo.Citas')
)
BEGIN
    ALTER TABLE dbo.Citas DROP CONSTRAINT CHK_EstatusCitas;
END;

ALTER TABLE dbo.Citas WITH CHECK ADD CONSTRAINT CHK_EstatusCitas
    CHECK ([estatus] IN ('PROGRAMADA', 'CONFIRMADA', 'ATENDIDA', 'CANCELADA', 'NO_ASISTIO', 'REPROGRAMADA', 'NO_CONFIRMADA'));
ALTER TABLE dbo.Citas CHECK CONSTRAINT CHK_EstatusCitas;
