using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class UnidadMedidaAreaM2M3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_unidades_medida_dimension",
                schema: "compartido",
                table: "unidades_medida");

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "unidades_medida",
                columns: new[] { "id", "codigo", "created_at", "created_by", "decimales", "deleted_at", "dimension", "es_base", "estatus", "factor_a_base", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0007-0000-0000-00000000000b"), "M2", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 2, null, (short)5, true, (short)0, 1m, "Metro cuadrado", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-00000000000c"), "M3", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 3, null, (short)2, false, (short)0, 1000m, "Metro cúbico", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_unidades_medida_dimension",
                schema: "compartido",
                table: "unidades_medida",
                sql: "dimension BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_unidades_medida_dimension",
                schema: "compartido",
                table: "unidades_medida");

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "unidades_medida",
                keyColumn: "id",
                keyValue: new Guid("00000002-0007-0000-0000-00000000000b"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "unidades_medida",
                keyColumn: "id",
                keyValue: new Guid("00000002-0007-0000-0000-00000000000c"));

            migrationBuilder.AddCheckConstraint(
                name: "ck_unidades_medida_dimension",
                schema: "compartido",
                table: "unidades_medida",
                sql: "dimension BETWEEN 0 AND 4");
        }
    }
}
