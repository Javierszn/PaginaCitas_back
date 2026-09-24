using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistroCivilAPI.Migrations
{
    /// <inheritdoc />
    public partial class InicialCloud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Avisos_Globales",
                columns: table => new
                {
                    id_aviso = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    titulo = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    mensaje = table.Column<string>(type: "text", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    imagen_url = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosGlobales", x => x.id_aviso);
                });

            migrationBuilder.CreateTable(
                name: "Categorias_Tramites",
                columns: table => new
                {
                    id_categoria = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre_categoria = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "varchar(255)", nullable: true),
                    activa = table.Column<bool>(type: "bit", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Categori__CD54BC5ADDE5FCC4", x => x.id_categoria);
                });

            migrationBuilder.CreateTable(
                name: "Ciudadanos",
                columns: table => new
                {
                    id_ciudadano = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    curp = table.Column<string>(type: "varchar(18)", unicode: false, maxLength: 18, nullable: false),
                    nombre = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    primer_apellido = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    segundo_apellido = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    correo = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    telefono = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    origen_registro = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Ciudadan__676FF54B1A98BECA", x => x.id_ciudadano);
                });

            migrationBuilder.CreateTable(
                name: "Configuracion_Agenda",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    fecha_inicio = table.Column<DateTime>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateTime>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuracion_Agenda", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Registro_Accesos",
                columns: table => new
                {
                    id_acceso = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    username = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    fecha_login = table.Column<DateTime>(type: "datetime", nullable: true),
                    fecha_logout = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registro_Accesos", x => x.id_acceso);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    id_rol = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre_rol = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Roles__6ABCB5E0D56A4E16", x => x.id_rol);
                });

            migrationBuilder.CreateTable(
                name: "Sedes",
                columns: table => new
                {
                    id_sede = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    municipio = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    direccion = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    activa = table.Column<bool>(type: "bit", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Sedes__D693504B010B634E", x => x.id_sede);
                });

            migrationBuilder.CreateTable(
                name: "Tramites",
                columns: table => new
                {
                    id_tramite = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_categoria = table.Column<int>(type: "int", nullable: false),
                    nombre_tramite = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    duracion_minutos = table.Column<int>(type: "int", nullable: false),
                    limite_diario_sede = table.Column<int>(type: "int", nullable: false),
                    costo = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    requisitos = table.Column<string>(type: "varchar(500)", nullable: true),
                    fecha_inicio_permitida = table.Column<DateTime>(type: "date", nullable: true),
                    fecha_fin_permitida = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Tramites__DC2729AE6A30BD91", x => x.id_tramite);
                    table.ForeignKey(
                        name: "FK__Tramites__id_cat__44FF419A",
                        column: x => x.id_categoria,
                        principalTable: "Categorias_Tramites",
                        principalColumn: "id_categoria");
                });

            migrationBuilder.CreateTable(
                name: "Dias_Inhabiles",
                columns: table => new
                {
                    id_dia_inhabil = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_sede = table.Column<int>(type: "int", nullable: true),
                    fecha_bloqueada = table.Column<DateOnly>(type: "date", nullable: false),
                    motivo = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Dias_Inh__2488E88C2B3103CD", x => x.id_dia_inhabil);
                    table.ForeignKey(
                        name: "FK__Dias_Inha__id_se__5441852A",
                        column: x => x.id_sede,
                        principalTable: "Sedes",
                        principalColumn: "id_sede");
                });

            migrationBuilder.CreateTable(
                name: "Horarios_Sede",
                columns: table => new
                {
                    id_horario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_sede = table.Column<int>(type: "int", nullable: false),
                    dia_semana = table.Column<byte>(type: "tinyint", nullable: false),
                    hora_apertura = table.Column<TimeOnly>(type: "time", nullable: false),
                    hora_cierre = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Horarios__C5836D69FA1A6428", x => x.id_horario);
                    table.ForeignKey(
                        name: "FK__Horarios___id_se__5165187F",
                        column: x => x.id_sede,
                        principalTable: "Sedes",
                        principalColumn: "id_sede");
                });

            migrationBuilder.CreateTable(
                name: "Usuarios_Internos",
                columns: table => new
                {
                    id_usuario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    username = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    nombre_completo = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    id_rol = table.Column<int>(type: "int", nullable: false),
                    id_sede = table.Column<int>(type: "int", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    requiere_cambio_password = table.Column<bool>(type: "bit", nullable: true),
                    IntentosFallidos = table.Column<int>(type: "int", nullable: false),
                    BloqueadoHasta = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Usuarios__4E3E04ADB8D06CD7", x => x.id_usuario);
                    table.ForeignKey(
                        name: "FK__Usuarios___id_ro__3E52440B",
                        column: x => x.id_rol,
                        principalTable: "Roles",
                        principalColumn: "id_rol");
                    table.ForeignKey(
                        name: "FK__Usuarios___id_se__3F466844",
                        column: x => x.id_sede,
                        principalTable: "Sedes",
                        principalColumn: "id_sede");
                });

            migrationBuilder.CreateTable(
                name: "Citas",
                columns: table => new
                {
                    id_cita = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    id_ciudadano = table.Column<int>(type: "int", nullable: false),
                    id_tramite = table.Column<int>(type: "int", nullable: false),
                    id_sede = table.Column<int>(type: "int", nullable: false),
                    fecha_hora_inicio = table.Column<DateTime>(type: "datetime", nullable: false),
                    fecha_hora_fin = table.Column<DateTime>(type: "datetime", nullable: false),
                    estatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ip_origen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    navegador = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    sistema_operativo = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Citas__6AEC3C092F0BF515", x => x.id_cita);
                    table.ForeignKey(
                        name: "FK__Citas__id_ciudad__4CA06362",
                        column: x => x.id_ciudadano,
                        principalTable: "Ciudadanos",
                        principalColumn: "id_ciudadano");
                    table.ForeignKey(
                        name: "FK__Citas__id_sede__4E88ABD4",
                        column: x => x.id_sede,
                        principalTable: "Sedes",
                        principalColumn: "id_sede");
                    table.ForeignKey(
                        name: "FK__Citas__id_tramit__4D94879B",
                        column: x => x.id_tramite,
                        principalTable: "Tramites",
                        principalColumn: "id_tramite");
                });

            migrationBuilder.CreateTable(
                name: "Bitacora_Auditoria",
                columns: table => new
                {
                    id_bitacora = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_usuario_interno = table.Column<int>(type: "int", nullable: false),
                    tabla_afectada = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    accion_realizada = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    registro_id = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    fecha_cambio = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Bitacora__7E4268B0D2EB1BA4", x => x.id_bitacora);
                    table.ForeignKey(
                        name: "FK__Bitacora___id_us__59063A47",
                        column: x => x.id_usuario_interno,
                        principalTable: "Usuarios_Internos",
                        principalColumn: "id_usuario");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bitacora_Auditoria_id_usuario_interno",
                table: "Bitacora_Auditoria",
                column: "id_usuario_interno");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_id_ciudadano",
                table: "Citas",
                column: "id_ciudadano");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_id_sede",
                table: "Citas",
                column: "id_sede");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_id_tramite",
                table: "Citas",
                column: "id_tramite");

            migrationBuilder.CreateIndex(
                name: "UQ__Ciudadan__2CDDD194822D2264",
                table: "Ciudadanos",
                column: "curp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dias_Inhabiles_id_sede",
                table: "Dias_Inhabiles",
                column: "id_sede");

            migrationBuilder.CreateIndex(
                name: "IX_Horarios_Sede_id_sede",
                table: "Horarios_Sede",
                column: "id_sede");

            migrationBuilder.CreateIndex(
                name: "IX_Tramites_id_categoria",
                table: "Tramites",
                column: "id_categoria");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Internos_id_rol",
                table: "Usuarios_Internos",
                column: "id_rol");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Internos_id_sede",
                table: "Usuarios_Internos",
                column: "id_sede");

            migrationBuilder.CreateIndex(
                name: "UQ__Usuarios__F3DBC572531AC299",
                table: "Usuarios_Internos",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Avisos_Globales");

            migrationBuilder.DropTable(
                name: "Bitacora_Auditoria");

            migrationBuilder.DropTable(
                name: "Citas");

            migrationBuilder.DropTable(
                name: "Configuracion_Agenda");

            migrationBuilder.DropTable(
                name: "Dias_Inhabiles");

            migrationBuilder.DropTable(
                name: "Horarios_Sede");

            migrationBuilder.DropTable(
                name: "Registro_Accesos");

            migrationBuilder.DropTable(
                name: "Usuarios_Internos");

            migrationBuilder.DropTable(
                name: "Ciudadanos");

            migrationBuilder.DropTable(
                name: "Tramites");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Sedes");

            migrationBuilder.DropTable(
                name: "Categorias_Tramites");
        }
    }
}
