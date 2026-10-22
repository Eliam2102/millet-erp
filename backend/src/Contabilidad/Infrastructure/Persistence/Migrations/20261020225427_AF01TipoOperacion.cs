using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF01TipoOperacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "contabilidad",
                table: "tipos_documento_contable",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "empresa_id", "es_prueba", "estatus", "nombre", "tipo_sat", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000f-0003-0000-0000-000000000001"), "AF", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, new Guid("00000003-0000-0000-0000-000000000001"), false, (short)0, "Capitalización de activos fijos", (short)3, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "contabilidad",
                table: "tipos_documento_contable",
                keyColumn: "id",
                keyValue: new Guid("0000000f-0003-0000-0000-000000000001"));
        }
    }
}
