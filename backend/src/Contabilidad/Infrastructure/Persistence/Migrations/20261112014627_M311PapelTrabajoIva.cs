using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M311PapelTrabajoIva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "liquidaciones_iva_foto",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_poliza = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resumen_json = table.Column<string>(type: "jsonb", nullable: false),
                    detalle_json = table.Column<string>(type: "jsonb", nullable: false),
                    cuadre_json = table.Column<string>(type: "jsonb", nullable: false),
                    tratamientos_json = table.Column<string>(type: "jsonb", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creada_por = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones_iva_foto", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diferencias_fiscales_iva",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    foto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    saldo_iva_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    uuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    iva_foto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_actual = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    diferencia = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    causa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    ajuste_poliza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detectada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrada_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diferencias_fiscales_iva", x => x.id);
                    table.ForeignKey(
                        name: "fk_diferencias_fiscales_iva_liquidaciones_iva_foto_foto_id",
                        column: x => x.foto_id,
                        principalSchema: "contabilidad",
                        principalTable: "liquidaciones_iva_foto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "liquidaciones_iva_acuse",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    foto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo_contenido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contenido = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    adjuntado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    adjuntado_por = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones_iva_acuse", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidaciones_iva_acuse_liquidaciones_iva_foto_foto_id",
                        column: x => x.foto_id,
                        principalSchema: "contabilidad",
                        principalTable: "liquidaciones_iva_foto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diferencias_fiscales_iva_estado",
                schema: "contabilidad",
                table: "diferencias_fiscales_iva",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_diferencias_fiscales_iva_foto_saldo",
                schema: "contabilidad",
                table: "diferencias_fiscales_iva",
                columns: new[] { "foto_id", "saldo_iva_documento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_liquidaciones_iva_acuse_foto",
                schema: "contabilidad",
                table: "liquidaciones_iva_acuse",
                column: "foto_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_liquidaciones_iva_foto_mes",
                schema: "contabilidad",
                table: "liquidaciones_iva_foto",
                columns: new[] { "empresa_id", "anio", "mes" });

            migrationBuilder.CreateIndex(
                name: "ux_liquidaciones_iva_foto_poliza",
                schema: "contabilidad",
                table: "liquidaciones_iva_foto",
                column: "poliza_id",
                unique: true);

            // M-311: la foto del mes y su acuse son inmutables también en la base (ni UPDATE ni DELETE desde ningún código).
            migrationBuilder.Sql(@"CREATE FUNCTION contabilidad.rechazar_cambio_papel_trabajo_iva() RETURNS trigger AS $$
BEGIN RAISE EXCEPTION 'M-311: % es inmutable (no admite UPDATE ni DELETE)', TG_TABLE_NAME; END; $$ LANGUAGE plpgsql;
CREATE TRIGGER trg_liquidaciones_iva_foto_inmutable BEFORE UPDATE OR DELETE ON contabilidad.liquidaciones_iva_foto
FOR EACH ROW EXECUTE FUNCTION contabilidad.rechazar_cambio_papel_trabajo_iva();
CREATE TRIGGER trg_liquidaciones_iva_acuse_inmutable BEFORE UPDATE OR DELETE ON contabilidad.liquidaciones_iva_acuse
FOR EACH ROW EXECUTE FUNCTION contabilidad.rechazar_cambio_papel_trabajo_iva();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_liquidaciones_iva_acuse_inmutable ON contabilidad.liquidaciones_iva_acuse; DROP TRIGGER IF EXISTS trg_liquidaciones_iva_foto_inmutable ON contabilidad.liquidaciones_iva_foto; DROP FUNCTION IF EXISTS contabilidad.rechazar_cambio_papel_trabajo_iva();");

            migrationBuilder.DropTable(
                name: "diferencias_fiscales_iva",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "liquidaciones_iva_acuse",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "liquidaciones_iva_foto",
                schema: "contabilidad");
        }
    }
}
