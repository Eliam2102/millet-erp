using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ParametrosUmbralesConteo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "parametros_globales",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "tipo", "updated_at", "updated_by", "valor", "version" },
                values: new object[,]
                {
                    { new Guid("00000006-0001-0000-0000-000000000009"), "almacen.conteo-variacion-pct-recuento", new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Exige recuento cuando la diferencia en cantidad supera este porcentaje. Aplica a conteos nuevos al iniciarlos.", "almacen", (short)1, new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "5", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000006"), "almacen.conteo-variacion-valor-recuento", new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Exige recuento cuando el valor de la diferencia supera este importe en MXN. Aplica a conteos nuevos al iniciarlos.", "almacen", (short)1, new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "1000", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000007"), "almacen.conteo-nivel1-maximo", new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Importe máximo en MXN que puede aprobar el almacenista, incluido este monto. Debe ser menor que el máximo del Nivel 2.", "almacen", (short)1, new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "1000", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000008"), "almacen.conteo-nivel2-maximo", new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Importe máximo en MXN que puede aprobar el supervisor, incluido este monto. Por encima aprueba el Jefe de Almacén y se avisa a Finanzas.", "almacen", (short)1, new DateTimeOffset(new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "10000", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000008"));
        }
    }
}
