using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P12TasasFiscalInpc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "tasa_fiscal_anual",
                schema: "activos_fijos",
                table: "clases",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tasa_contable_anual",
                schema: "activos_fijos",
                table: "clases",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "activo_mejorado_id",
                schema: "activos_fijos",
                table: "activos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "vida_util_meses",
                schema: "activos_fijos",
                table: "activos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inpc_mensual",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inpc_mensual", x => x.id);
                    table.CheckConstraint("ck_inpc_valido", "anio BETWEEN 1 AND 9998 AND mes BETWEEN 1 AND 12 AND valor > 0");
                });

            migrationBuilder.Sql("UPDATE activos_fijos.clases SET tasa_contable_anual = tasa_fiscal_anual;");
            // Sólo sustituye sugerencias: conserva la captura confirmada de cualquier empresa.
            migrationBuilder.Sql("""
                UPDATE activos_fijos.clases AS c
                SET tasa_contable_anual = v.contable, tasa_fiscal_anual = v.fiscal,
                    tasa_confirmada = (c.clave <> '156'), depreciable = v.depreciable
                FROM (VALUES
                    ('151', 0::numeric, 0::numeric, false), ('152', 5, 5, true),
                    ('153', 10, 10, true), ('154', 25, 25, true), ('155', 10, 10, true),
                    ('156', 33, 30, true), ('157', 10, 10, true), ('159', NULL, NULL, false),
                    ('160', NULL, NULL, true), ('165', 10, 10, true), ('166', 10, 10, true),
                    ('169', 10, 10, true), ('170', NULL, NULL, true)
                ) AS v(clave, contable, fiscal, depreciable)
                WHERE c.clave = v.clave AND c.tasa_confirmada = false;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_activos_activo_mejorado_id",
                schema: "activos_fijos",
                table: "activos",
                column: "activo_mejorado_id");

            migrationBuilder.CreateIndex(
                name: "ix_inpc_mensual_empresa_id_anio_mes",
                schema: "activos_fijos",
                table: "inpc_mensual",
                columns: new[] { "empresa_id", "anio", "mes" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_activos_activos_activo_mejorado_id",
                schema: "activos_fijos",
                table: "activos",
                column: "activo_mejorado_id",
                principalSchema: "activos_fijos",
                principalTable: "activos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("P12 conserva tasas capturadas e INPC; restaure un respaldo para revertir sin pérdida de datos.");
    }
}
