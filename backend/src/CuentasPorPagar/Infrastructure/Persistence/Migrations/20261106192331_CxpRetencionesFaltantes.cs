using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CxpRetencionesFaltantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "cuentas_por_pagar",
                table: "retenciones_concepto",
                columns: new[] { "id", "activa", "concepto", "created_at", "created_by", "deleted_at", "descripcion", "fuente", "impuesto", "motivo_cambio", "tasa", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000007-000a-0000-0000-000000000007"), true, "SERVICIOS", new DateTimeOffset(new DateTime(2026, 11, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-86e3zcbjw", null, "IVA: servicios de personal o subcontratación recibidos por persona moral (art. 1-A fr. IV LIVA)", "https://www.diputados.gob.mx/LeyesBiblio/pdf/LIVA.pdf", "002", "Supuesto SAT, valida Fiscal (D03)", 0.06m, new DateTimeOffset(new DateTime(2026, 11, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-86e3zcbjw", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000008"), true, "RESICO_PF_SERVICIOS", new DateTimeOffset(new DateTime(2026, 11, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-86e3zcbjw", null, "Servicios o arrendamiento de persona física RESICO a persona moral", "https://wwwmat.sat.gob.mx/articulo/59511/articulo-113-j", "001", "Supuesto SAT, valida Fiscal (D03)", 0.0125m, new DateTimeOffset(new DateTime(2026, 11, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-86e3zcbjw", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000009"), true, "RESICO_PF_SERVICIOS", new DateTimeOffset(new DateTime(2026, 11, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-86e3zcbjw", null, "IVA: dos terceras partes a tasa general de 16 % (RESICO con servicios o arrendamiento)", "https://www.diputados.gob.mx/LeyesBiblio/pdf/LIVA.pdf", "002", "Supuesto SAT, valida Fiscal (D03)", 0.10666667m, new DateTimeOffset(new DateTime(2026, 11, 6, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-86e3zcbjw", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "cuentas_por_pagar",
                table: "retenciones_concepto",
                keyColumn: "id",
                keyValue: new Guid("00000007-000a-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "cuentas_por_pagar",
                table: "retenciones_concepto",
                keyColumn: "id",
                keyValue: new Guid("00000007-000a-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                schema: "cuentas_por_pagar",
                table: "retenciones_concepto",
                keyColumn: "id",
                keyValue: new Guid("00000007-000a-0000-0000-000000000009"));
        }
    }
}
