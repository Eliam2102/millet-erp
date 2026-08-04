using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SeedDepartamentoSistemaReorden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "departamentos",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "estatus", "nombre", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000d-0001-0000-0000-000000000001"), "SIS-REAB", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Reabastecimiento Automático", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "departamentos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0001-0000-0000-000000000001"));
        }
    }
}
