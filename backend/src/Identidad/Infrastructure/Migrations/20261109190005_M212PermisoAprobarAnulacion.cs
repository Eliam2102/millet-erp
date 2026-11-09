using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M212PermisoAprobarAnulacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0005-0000-0000-000000000004"),
                column: "descripcion",
                value: "Solicitar con motivo la anulación de pólizas autorizadas (la aprueba otra persona)");

            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000d-0005-0000-0000-000000000005"), "anular-aprobar", "contabilidad.polizas.anular-aprobar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Aprobar o rechazar la anulación de pólizas solicitada por otra persona (genera la póliza con las mismas partidas en negativo)", "contabilidad", "polizas", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0005-0000-0000-000000000005"));

            migrationBuilder.UpdateData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0005-0000-0000-000000000004"),
                column: "descripcion",
                value: "Anular pólizas autorizadas con motivo (genera la póliza con las mismas partidas en negativo)");
        }
    }
}
