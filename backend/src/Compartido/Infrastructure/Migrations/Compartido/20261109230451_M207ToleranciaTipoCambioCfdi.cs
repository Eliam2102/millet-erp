using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class M207ToleranciaTipoCambioCfdi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "parametros_globales",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "tipo", "updated_at", "updated_by", "valor", "version" },
                values: new object[] { new Guid("00000006-0001-0000-0000-00000000000d"), "contabilidad.tipo-cambio.tolerancia-pct", new DateTimeOffset(new DateTime(2026, 11, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Diferencia máxima (%) entre el tipo de cambio de un CFDI en moneda extranjera y el del catálogo para su fecha. Fuera de ella, la póliza va a Rechazos para autorizar un valor. 0 apaga la comparación.", "contabilidad", (short)1, new DateTimeOffset(new DateTime(2026, 11, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "1.00", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-00000000000d"));
        }
    }
}
