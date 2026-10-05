using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadReglasDimensionUso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reglas_dimension_uso",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumidor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fecha_contable = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reglas_dimension_uso", x => x.id);
                    table.ForeignKey(
                        name: "fk_reglas_dimension_uso_reglas_dimension_regla_id",
                        column: x => x.regla_id,
                        principalSchema: "contabilidad",
                        principalTable: "reglas_dimension",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reglas_dimension_uso_regla_id",
                schema: "contabilidad",
                table: "reglas_dimension_uso",
                column: "regla_id");

            migrationBuilder.CreateIndex(
                name: "ux_reglas_dimension_uso",
                schema: "contabilidad",
                table: "reglas_dimension_uso",
                columns: new[] { "empresa_id", "regla_id", "consumidor", "referencia" },
                unique: true);

            // Relleno: los movimientos de prueba ya registrados guardan en reglas_aplicadas (jsonb) la regla con que se validaron.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.reglas_dimension_uso
                    (id, empresa_id, regla_id, consumidor, referencia, fecha_contable, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), m.empresa_id, (r->>'reglaId')::uuid, 'MOVIMIENTO_PRUEBA', m.id::text, m.fecha_contable,
                       1, now(), now(), 'migracion', 'migracion'
                FROM contabilidad.movimientos_dimension_prueba m
                CROSS JOIN LATERAL jsonb_array_elements(m.reglas_aplicadas) r
                WHERE r->>'reglaId' IS NOT NULL
                  AND EXISTS (SELECT 1 FROM contabilidad.reglas_dimension d WHERE d.id = (r->>'reglaId')::uuid)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reglas_dimension_uso",
                schema: "contabilidad");
        }
    }
}
