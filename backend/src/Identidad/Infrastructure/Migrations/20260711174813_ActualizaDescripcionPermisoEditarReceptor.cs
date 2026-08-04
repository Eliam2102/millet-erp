using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActualizaDescripcionPermisoEditarReceptor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-0002-0000-0000-000000000003"),
                column: "descripcion",
                value: "Corregir los datos fiscales del receptor desde la emisión: muestra el acceso directo al catálogo de clientes (los datos son de solo lectura en la factura)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-0002-0000-0000-000000000003"),
                column: "descripcion",
                value: "Editar datos fiscales del receptor en la emisión (captura excepcional; el default es tomarlos fijos del master de clientes)");
        }
    }
}
