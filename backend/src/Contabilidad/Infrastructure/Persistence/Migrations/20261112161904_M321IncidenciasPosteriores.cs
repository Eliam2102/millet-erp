using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M321IncidenciasPosteriores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidencias_posteriores",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    periodo_anio = table.Column<int>(type: "integer", nullable: false),
                    periodo_numero = table.Column<int>(type: "integer", nullable: false),
                    hallazgo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    impacto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    importe_impacto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    registrada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registrada_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolucion = table.Column<short>(type: "smallint", nullable: true),
                    poliza_ajuste_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reapertura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota_resolucion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resuelta_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidencias_posteriores", x => x.id);
                    table.CheckConstraint("ck_incidencias_posteriores_estado", "estado BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_incidencias_posteriores_resolucion", "resolucion IS NULL OR resolucion BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "fk_incidencias_posteriores_polizas_poliza_id",
                        column: x => x.poliza_id,
                        principalSchema: "contabilidad",
                        principalTable: "polizas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_posteriores_empresa_id_periodo_anio_periodo_num",
                schema: "contabilidad",
                table: "incidencias_posteriores",
                columns: new[] { "empresa_id", "periodo_anio", "periodo_numero", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_posteriores_poliza_id",
                schema: "contabilidad",
                table: "incidencias_posteriores",
                column: "poliza_id",
                filter: "poliza_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidencias_posteriores",
                schema: "contabilidad");
        }
    }
}
