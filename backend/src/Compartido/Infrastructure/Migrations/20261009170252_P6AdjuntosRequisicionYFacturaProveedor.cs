using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P6AdjuntosRequisicionYFacturaProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                columns: new[] { "id", "activo", "codigo", "created_at", "created_by", "deleted_at", "nombre", "obligatorio", "orden", "solo_persona_moral", "tipo_entidad", "updated_at", "updated_by", "version", "vigencia_meses" },
                values: new object[,]
                {
                    { new Guid("00000011-0006-0000-0000-000000000001"), true, "soporte", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Documento de soporte", false, 1, false, "requisicion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("00000011-0006-0000-0000-000000000002"), true, "soporte", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Documento de soporte", false, 1, false, "factura_proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0006-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("00000011-0006-0000-0000-000000000002"));
        }
    }
}
