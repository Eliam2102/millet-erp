using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class P200UnidadesEmpaqueSat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P-200 (Fernando Alejos 4-nov). SQL en vez de InsertData: si un ambiente ya dio de alta el código a mano, no falla.
            migrationBuilder.Sql("""
                INSERT INTO compartido.unidades_medida
                    (id, codigo, nombre, dimension, factor_a_base, decimales, es_base, estatus, version, created_at, created_by, updated_at, updated_by, deleted_at)
                VALUES
                    ('00000002-0007-0000-0000-00000000000d', 'CAJA', 'Caja (SAT XBX)', 0, 1, 0, false, 0, 1, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'seed', TIMESTAMPTZ '2026-01-01 00:00:00+00', 'seed', NULL),
                    ('00000002-0007-0000-0000-00000000000e', 'ROLLO', 'Rollo (SAT RK)', 0, 1, 0, false, 0, 1, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'seed', TIMESTAMPTZ '2026-01-01 00:00:00+00', 'seed', NULL),
                    ('00000002-0007-0000-0000-00000000000f', 'PAQ', 'Paquete (SAT XPK)', 0, 1, 0, false, 0, 1, TIMESTAMPTZ '2026-01-01 00:00:00+00', 'seed', TIMESTAMPTZ '2026-01-01 00:00:00+00', 'seed', NULL)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "unidades_medida",
                keyColumn: "id",
                keyValue: new Guid("00000002-0007-0000-0000-00000000000d"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "unidades_medida",
                keyColumn: "id",
                keyValue: new Guid("00000002-0007-0000-0000-00000000000e"));

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "unidades_medida",
                keyColumn: "id",
                keyValue: new Guid("00000002-0007-0000-0000-00000000000f"));
        }
    }
}
