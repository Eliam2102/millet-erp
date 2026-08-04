using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SeedRegimenesFiscalesCompletos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "regimenes_fiscales",
                columns: new[] { "id", "aplica_persona_fisica", "codigo", "created_at", "created_by", "deleted_at", "estatus", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0002-0000-0000-000000000009"), true, "607", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Régimen de Enajenación o Adquisición de Bienes", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000010"), true, "608", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Demás ingresos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000011"), false, "610", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Residentes en el Extranjero sin Establecimiento Permanente en México", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000012"), true, "611", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Ingresos por Dividendos (socios y accionistas)", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000013"), true, "614", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Ingresos por intereses", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000014"), true, "615", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Régimen de los ingresos por obtención de premios", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000015"), true, "616", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Sin obligaciones fiscales", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000016"), false, "620", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Sociedades Cooperativas de Producción que optan por diferir sus ingresos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000017"), false, "622", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000018"), false, "623", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Opcional para Grupos de Sociedades", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000019"), false, "624", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Coordinados", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000010"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000011"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000012"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000014"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000015"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000016"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000017"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000018"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "regimenes_fiscales",
                keyColumn: "id",
                keyValue: new Guid("00000002-0002-0000-0000-000000000019"));
        }
    }
}
