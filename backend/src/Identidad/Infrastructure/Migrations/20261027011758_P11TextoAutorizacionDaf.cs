using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P11TextoAutorizacionDaf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-0008-0000-0000-000000000001"),
                column: "descripcion",
                value: "Autorizar la venta de activos fijos antes de timbrar (Director de Administración y Finanzas)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-0008-0000-0000-000000000001"),
                column: "descripcion",
                value: "Autorizar la venta de activos fijos antes de timbrar (Contador General)");
        }
    }
}
