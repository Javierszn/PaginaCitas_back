using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RegistroCivilAPI.Models;

#nullable disable

namespace RegistroCivilAPI.Migrations
{
    [DbContext(typeof(RegistroCivilCitasContext))]
    [Migration("20261103120000_AgregarEstatusNoConfirmada")]
    public sealed class AgregarEstatusNoConfirmada : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM sys.check_constraints
                    WHERE name = N'CHK_EstatusCitas'
                      AND parent_object_id = OBJECT_ID(N'dbo.Citas'))
                BEGIN
                    ALTER TABLE dbo.Citas DROP CONSTRAINT CHK_EstatusCitas;
                END;

                ALTER TABLE dbo.Citas WITH CHECK ADD CONSTRAINT CHK_EstatusCitas
                    CHECK ([estatus] IN ('PROGRAMADA', 'CONFIRMADA', 'ATENDIDA', 'CANCELADA', 'NO_ASISTIO', 'REPROGRAMADA', 'NO_CONFIRMADA'));
                ALTER TABLE dbo.Citas CHECK CONSTRAINT CHK_EstatusCitas;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE dbo.Citas SET estatus = 'PROGRAMADA' WHERE estatus = 'NO_CONFIRMADA';

                IF EXISTS (
                    SELECT 1 FROM sys.check_constraints
                    WHERE name = N'CHK_EstatusCitas'
                      AND parent_object_id = OBJECT_ID(N'dbo.Citas'))
                BEGIN
                    ALTER TABLE dbo.Citas DROP CONSTRAINT CHK_EstatusCitas;
                END;

                ALTER TABLE dbo.Citas WITH CHECK ADD CONSTRAINT CHK_EstatusCitas
                    CHECK ([estatus] IN ('PROGRAMADA', 'CONFIRMADA', 'ATENDIDA', 'CANCELADA', 'NO_ASISTIO', 'REPROGRAMADA'));
                ALTER TABLE dbo.Citas CHECK CONSTRAINT CHK_EstatusCitas;
                """);
        }
    }
}
