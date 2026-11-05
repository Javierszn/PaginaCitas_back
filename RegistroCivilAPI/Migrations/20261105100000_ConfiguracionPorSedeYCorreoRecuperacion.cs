using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RegistroCivilAPI.Models;

#nullable disable

namespace RegistroCivilAPI.Migrations
{
    [DbContext(typeof(RegistroCivilCitasContext))]
    [Migration("20261105100000_ConfiguracionPorSedeYCorreoRecuperacion")]
    public partial class ConfiguracionPorSedeYCorreoRecuperacion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Peticiones_Soporte', N'correo_contacto') IS NULL
    ALTER TABLE dbo.Peticiones_Soporte ADD correo_contacto varchar(320) NULL;

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
  );");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Peticiones_Soporte', N'correo_contacto') IS NOT NULL
    ALTER TABLE dbo.Peticiones_Soporte DROP COLUMN correo_contacto;");
        }
    }
}
