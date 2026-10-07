using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadCatalogoInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contabilidad");

            migrationBuilder.CreateTable(
                name: "cuentas_contables",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    padre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nivel = table.Column<short>(type: "smallint", nullable: false),
                    naturaleza = table.Column<short>(type: "smallint", nullable: true),
                    tipo = table.Column<short>(type: "smallint", nullable: true),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    cuenta_control = table.Column<short>(type: "smallint", nullable: false),
                    codigo_agrupador = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    grupo_reporte = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuentas_contables", x => x.id);
                    table.CheckConstraint("ck_cuentas_control_afectable", "cuenta_control = 0 OR COALESCE(tipo, -1) = 1");
                    table.CheckConstraint("ck_cuentas_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_cuentas_nivel", "nivel >= 1");
                    table.ForeignKey(
                        name: "fk_cuentas_contables_cuentas_contables_padre_id",
                        column: x => x.padre_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "importaciones_catalogo",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuente = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    archivo_nombre = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    huella_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    total_filas = table.Column<int>(type: "integer", nullable: false),
                    creadas = table.Column<int>(type: "integer", nullable: false),
                    actualizadas = table.Column<int>(type: "integer", nullable: false),
                    sin_cambios = table.Column<int>(type: "integer", nullable: false),
                    aplicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aplicado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_importaciones_catalogo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cuentas_contables_uso",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumidor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    primer_uso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuentas_contables_uso", x => x.id);
                    table.ForeignKey(
                        name: "fk_cuentas_contables_uso_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cuentas_contables_origen",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuente = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    codigo_origen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuentas_contables_origen", x => x.id);
                    table.ForeignKey(
                        name: "fk_cuentas_contables_origen_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cuentas_contables_origen_importaciones_lote_id",
                        column: x => x.lote_id,
                        principalSchema: "contabilidad",
                        principalTable: "importaciones_catalogo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_empresa_id_estatus_tipo",
                schema: "contabilidad",
                table: "cuentas_contables",
                columns: new[] { "empresa_id", "estatus", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_empresa_id_padre_id",
                schema: "contabilidad",
                table: "cuentas_contables",
                columns: new[] { "empresa_id", "padre_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_padre_id",
                schema: "contabilidad",
                table: "cuentas_contables",
                column: "padre_id");

            migrationBuilder.CreateIndex(
                name: "ux_cuentas_contables_codigo",
                schema: "contabilidad",
                table: "cuentas_contables",
                columns: new[] { "empresa_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_origen_cuenta_id",
                schema: "contabilidad",
                table: "cuentas_contables_origen",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_origen_lote_id",
                schema: "contabilidad",
                table: "cuentas_contables_origen",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ux_cuentas_origen_fuente_codigo",
                schema: "contabilidad",
                table: "cuentas_contables_origen",
                columns: new[] { "empresa_id", "fuente", "codigo_origen" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_uso_cuenta_id",
                schema: "contabilidad",
                table: "cuentas_contables_uso",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_uso_empresa_id_cuenta_id",
                schema: "contabilidad",
                table: "cuentas_contables_uso",
                columns: new[] { "empresa_id", "cuenta_id" });

            migrationBuilder.CreateIndex(
                name: "ux_importaciones_huella",
                schema: "contabilidad",
                table: "importaciones_catalogo",
                columns: new[] { "empresa_id", "huella_sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cuentas_contables_origen",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "cuentas_contables_uso",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "importaciones_catalogo",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "cuentas_contables",
                schema: "contabilidad");
        }
    }
}
