using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class P1CalendarioHabilFestivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "parametros_globales",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "tipo", "updated_at", "updated_by", "valor", "version" },
                values: new object[] { new Guid("00000006-0001-0000-0000-00000000000a"), "system.dias-festivos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Descansos obligatorios LFT art. 74 de 2026 y 2027: dato a validar por Millet. Agregar fechas electorales aplicables. Lista JSON AAAA-MM-DD.", null, (short)3, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "[\"2026-01-01\",\"2026-02-02\",\"2026-03-16\",\"2026-05-01\",\"2026-09-16\",\"2026-11-16\",\"2026-12-25\",\"2027-01-01\",\"2027-02-01\",\"2027-03-15\",\"2027-05-01\",\"2027-06-06\",\"2027-09-16\",\"2027-11-15\",\"2027-12-25\"]", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-00000000000a"));
        }
    }
}
