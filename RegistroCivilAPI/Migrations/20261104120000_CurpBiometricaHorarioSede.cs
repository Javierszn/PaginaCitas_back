using Microsoft.EntityFrameworkCore.Migrations;
using RegistroCivilAPI.Models;

#nullable disable

namespace RegistroCivilAPI.Migrations
{
    public partial class CurpBiometricaHorarioSede : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "id_sede",
                table: "Tramites",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "hora_apertura",
                table: "Tramites",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "hora_cierre",
                table: "Tramites",
                type: "time",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tramites_id_sede",
                table: "Tramites",
                column: "id_sede");

            migrationBuilder.AddForeignKey(
                name: "FK_Tramites_Sedes_id_sede",
                table: "Tramites",
                column: "id_sede",
                principalTable: "Sedes",
                principalColumn: "id_sede",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
DECLARE @CategoriaCurp int = (
    SELECT TOP (1) id_categoria
    FROM dbo.Categorias_Tramites
    WHERE id_categoria = 4 AND nombre_categoria LIKE N'%CURP%'
);
DECLARE @SedeCentro int = (
    SELECT TOP (1) id_sede FROM dbo.Sedes
    WHERE nombre LIKE N'%Zona Centro%' ORDER BY id_sede
);
IF @CategoriaCurp IS NULL THROW 51002, 'No se encontró la categoría de CURP.', 1;
IF @SedeCentro IS NULL THROW 51001, 'No se encontró la sede Zona Centro.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.Tramites
    WHERE id_categoria = @CategoriaCurp
      AND id_sede = @SedeCentro
      AND nombre_tramite = N'CURP BIOMÉTRICA'
)
BEGIN
    INSERT dbo.Tramites
        (id_categoria, nombre_tramite, descripcion, duracion_minutos,
         limite_diario_sede, costo, activo, requisitos,
         fecha_inicio_permitida, fecha_fin_permitida,
         id_sede, hora_apertura, hora_cierre)
    VALUES
        (@CategoriaCurp, N'CURP BIOMÉTRICA',
         N'Registro de datos biométricos (huellas dactilares, iris y rostro) para fortalecer la seguridad e identidad.',
         15, 50, 0, 1,
         N'CURP impresa certificada por RENAPO; Identificación oficial vigente; Correo electrónico personal',
         NULL, NULL, @SedeCentro, '08:00', '15:00');
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE t
FROM dbo.Tramites t
JOIN dbo.Sedes s ON s.id_sede = t.id_sede
WHERE t.id_categoria = 4
  AND t.nombre_tramite = N'CURP BIOMÉTRICA'
  AND s.nombre LIKE N'%Zona Centro%';");
            migrationBuilder.DropForeignKey(name: "FK_Tramites_Sedes_id_sede", table: "Tramites");
            migrationBuilder.DropIndex(name: "IX_Tramites_id_sede", table: "Tramites");
            migrationBuilder.DropColumn(name: "id_sede", table: "Tramites");
            migrationBuilder.DropColumn(name: "hora_apertura", table: "Tramites");
            migrationBuilder.DropColumn(name: "hora_cierre", table: "Tramites");
        }
    }
}
