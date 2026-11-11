using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class M373TipoDocumentoCondicional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "condicional",
                schema: "compartido",
                table: "adjunto_tipos_documento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000006"),
                column: "condicional",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000b"),
                column: "condicional",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000c"),
                column: "condicional",
                value: true);

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000d"),
                column: "condicional",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "condicional",
                schema: "compartido",
                table: "adjunto_tipos_documento");
        }
    }
}
