using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CentrosCostoPermisosModeloDim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0001-0000-0000-000000000001"),
                column: "descripcion",
                value: "Consultar el catálogo de centros de costo (árboles, niveles y grupos)");

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0001-0000-0000-000000000002"),
                column: "descripcion",
                value: "Crear, editar y desactivar niveles y grupos del catálogo de centros de costo");

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0002-0000-0000-000000000001"),
                column: "descripcion",
                value: "Asignar y revocar alcance de centros de costo a usuarios (marcado por nivel o grupo, congelado en máquinas)");

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0003-0000-0000-000000000001"),
                columns: new[] { "codigo", "descripcion", "recurso" },
                values: new object[] { "centros_costo.dim3.leer-todos", "Alcance total en centros de costo: ver todas las máquinas (Dim3) sin restricción de asignación", "dim3" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0001-0000-0000-000000000001"),
                column: "descripcion",
                value: "Consultar el catálogo de centros de costo (árbol, niveles y dimensiones)");

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0001-0000-0000-000000000002"),
                column: "descripcion",
                value: "Crear, editar y desactivar nodos y dimensiones del catálogo de centros de costo");

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0002-0000-0000-000000000001"),
                column: "descripcion",
                value: "Asignar y revocar alcance de centros de costo a usuarios (departamento, subgrupo o equipo)");

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000c-0003-0000-0000-000000000001"),
                columns: new[] { "codigo", "descripcion", "recurso" },
                values: new object[] { "centros_costo.equipos.leer-todos", "Alcance total en centros de costo: ver todos los equipos sin restricción de asignación", "equipos" });
        }
    }
}
