using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ComercioExteriorTiposDocumentales : Migration
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
                    { new Guid("0000000e-0003-0000-0000-000000000001"), true, "IMP_FACTURA", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Factura comercial", true, 1, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000002"), true, "IMP_PACKING", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Packing list", true, 2, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000003"), true, "IMP_TRANSPORTE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "BL o AWB", true, 3, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000004"), true, "IMP_PEDIMENTO", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Pedimento", true, 4, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000005"), true, "IMP_OC", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Orden de compra", true, 5, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000006"), true, "IMP_REGULATORIOS", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Regulatorios", false, 6, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000007"), true, "EXP_PACKING", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Packing list", true, 7, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000008"), true, "EXP_FACTURA", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Factura comercial", true, 8, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-000000000009"), true, "EXP_BL", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "BL", true, 9, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-00000000000a"), true, "EXP_PEDIMENTO", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Pedimento", true, 10, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-00000000000b"), true, "EXP_ORIGEN", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Certificado de origen", false, 11, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-00000000000c"), true, "EXP_ISF", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "ISF", false, 12, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-00000000000d"), true, "EXP_CARTA_PORTE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Carta Porte", false, 13, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null },
                    { new Guid("0000000e-0003-0000-0000-00000000000e"), true, "EXP_POD", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "POD", false, 14, false, "expediente_ce", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000a"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000b"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000c"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000d"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "adjunto_tipos_documento",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0003-0000-0000-00000000000e"));
        }
    }
}
