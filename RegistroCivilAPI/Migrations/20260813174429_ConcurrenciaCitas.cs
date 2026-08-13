using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistroCivilAPI.Migrations
{
    /// <inheritdoc />
    public partial class ConcurrenciaCitas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Obligamos a EF Core a crear únicamente la columna del semáforo
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Citas",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Citas");
        }
    }
}
