using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadDimensionesReglas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "centros_costo_sucursal",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dim2_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_centros_costo_sucursal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tipos_documento_contable",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    es_prueba = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipos_documento_contable", x => x.id);
                    table.CheckConstraint("ck_tipos_documento_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "movimientos_dimension_prueba",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento_clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha_contable = table.Column<DateOnly>(type: "date", nullable: false),
                    dim1_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dim2_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dim3_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reglas_aplicadas = table.Column<string>(type: "jsonb", nullable: false),
                    confirmado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_dimension_prueba", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimientos_dimension_prueba_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimientos_dimension_prueba_tipos_documento_tipo_documento",
                        column: x => x.tipo_documento_id,
                        principalSchema: "contabilidad",
                        principalTable: "tipos_documento_contable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reglas_dimension",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dimension = table.Column<short>(type: "smallint", nullable: false),
                    requerimiento = table.Column<short>(type: "smallint", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    es_prueba = table.Column<bool>(type: "boolean", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reglas_dimension", x => x.id);
                    table.CheckConstraint("ck_reglas_dimension_dimension", "dimension BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_reglas_dimension_requerimiento", "requerimiento BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_reglas_dimension_vigencia", "vigente_hasta IS NULL OR vigente_hasta >= vigente_desde");
                    table.ForeignKey(
                        name: "fk_reglas_dimension_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reglas_dimension_tipos_documento_tipo_documento_id",
                        column: x => x.tipo_documento_id,
                        principalSchema: "contabilidad",
                        principalTable: "tipos_documento_contable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_centros_costo_sucursal_empresa_id_sucursal_id",
                schema: "contabilidad",
                table: "centros_costo_sucursal",
                columns: new[] { "empresa_id", "sucursal_id" });

            migrationBuilder.CreateIndex(
                name: "ux_centros_costo_sucursal",
                schema: "contabilidad",
                table: "centros_costo_sucursal",
                columns: new[] { "empresa_id", "dim2_id", "sucursal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_dimension_prueba_cuenta_id",
                schema: "contabilidad",
                table: "movimientos_dimension_prueba",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_dimension_prueba_empresa_id_cuenta_id",
                schema: "contabilidad",
                table: "movimientos_dimension_prueba",
                columns: new[] { "empresa_id", "cuenta_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_dimension_prueba_empresa_id_sucursal_id_confirm",
                schema: "contabilidad",
                table: "movimientos_dimension_prueba",
                columns: new[] { "empresa_id", "sucursal_id", "confirmado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_dimension_prueba_tipo_documento_id",
                schema: "contabilidad",
                table: "movimientos_dimension_prueba",
                column: "tipo_documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_reglas_dimension_cuenta_id",
                schema: "contabilidad",
                table: "reglas_dimension",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_reglas_dimension_empresa_id_vigente_desde_vigente_hasta",
                schema: "contabilidad",
                table: "reglas_dimension",
                columns: new[] { "empresa_id", "vigente_desde", "vigente_hasta" });

            migrationBuilder.CreateIndex(
                name: "ix_reglas_dimension_tipo_documento_id",
                schema: "contabilidad",
                table: "reglas_dimension",
                column: "tipo_documento_id");

            migrationBuilder.CreateIndex(
                name: "ux_reglas_dimension_inicio",
                schema: "contabilidad",
                table: "reglas_dimension",
                columns: new[] { "empresa_id", "cuenta_id", "tipo_documento_id", "dimension", "vigente_desde" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_tipos_documento_clave",
                schema: "contabilidad",
                table: "tipos_documento_contable",
                columns: new[] { "empresa_id", "clave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "centros_costo_sucursal",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "movimientos_dimension_prueba",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "reglas_dimension",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "tipos_documento_contable",
                schema: "contabilidad");
        }
    }
}
