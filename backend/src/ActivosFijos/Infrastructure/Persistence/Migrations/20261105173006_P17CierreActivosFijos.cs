using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P17CierreActivosFijos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cargas_libro_empresa_id_huella_version_archivo",
                schema: "activos_fijos",
                table: "cargas_libro");

            migrationBuilder.DropIndex(
                name: "ix_activos_empresa_id_numero",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.AddColumn<bool>(
                name: "corregido",
                schema: "activos_fijos",
                table: "traslados",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "corrige_a",
                schema: "activos_fijos",
                table: "traslados",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "capturado_en",
                schema: "activos_fijos",
                table: "partidas_conteo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_recaptura",
                schema: "activos_fijos",
                table: "partidas_conteo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_rechazo",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rechazado_en",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rechazado_por",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revierte_a",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "venta_cancelada",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "anulada",
                schema: "activos_fijos",
                table: "cargas_libro",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "motivo_anulacion",
                schema: "activos_fijos",
                table: "cargas_libro",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "documento_nuevo_id",
                schema: "activos_fijos",
                table: "avisos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_resolucion",
                schema: "activos_fijos",
                table: "avisos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolucion",
                schema: "activos_fijos",
                table: "avisos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resuelto_en",
                schema: "activos_fijos",
                table: "avisos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resuelto_por",
                schema: "activos_fijos",
                table: "avisos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "soporte_resolucion",
                schema: "activos_fijos",
                table: "avisos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "capitalizado_por",
                schema: "activos_fijos",
                table: "activos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "carga_anulada",
                schema: "activos_fijos",
                table: "activos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "tasa_contable_anual",
                schema: "activos_fijos",
                table: "activos",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tasa_fiscal_anual",
                schema: "activos_fijos",
                table: "activos",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "capturas_anteriores_conteo",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado = table.Column<string>(type: "text", nullable: false),
                    ubicacion_real = table.Column<string>(type: "text", nullable: true),
                    evidencia = table.Column<string>(type: "text", nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    capturado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_recaptura = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capturas_anteriores_conteo", x => x.id);
                    table.ForeignKey(
                        name: "fk_capturas_anteriores_conteo_partidas_conteo_partida_id",
                        column: x => x.partida_id,
                        principalSchema: "activos_fijos",
                        principalTable: "partidas_conteo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conciliaciones",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<DateOnly>(type: "date", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporte_json = table.Column<string>(type: "text", nullable: false),
                    clasificaciones_json = table.Column<string>(type: "text", nullable: false),
                    huella = table.Column<string>(type: "text", nullable: false),
                    aceptada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    aceptada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliaciones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "equivalencias_sap",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clase_sap = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    clave_erp = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equivalencias_sap", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "impresiones_etiqueta",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    impreso_por = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impresiones_etiqueta", x => x.id);
                    table.ForeignKey(
                        name: "fk_impresiones_etiqueta_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "versiones_clase",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    vigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    propuesto_por = table.Column<Guid>(type: "uuid", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "text", nullable: true),
                    tasa_contable_anual = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    tasa_fiscal_anual = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    cuenta_activo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_depreciacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_gasto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_puente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depreciable = table.Column<bool>(type: "boolean", nullable: false),
                    tasa_confirmada = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versiones_clase", x => x.id);
                    table.ForeignKey(
                        name: "fk_versiones_clase_clases_clase_id",
                        column: x => x.clase_id,
                        principalSchema: "activos_fijos",
                        principalTable: "clases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cargas_libro_empresa_id_huella_version_archivo",
                schema: "activos_fijos",
                table: "cargas_libro",
                columns: new[] { "empresa_id", "huella", "version_archivo" },
                unique: true,
                filter: "NOT anulada");

            migrationBuilder.CreateIndex(
                name: "ix_activos_empresa_id_numero",
                schema: "activos_fijos",
                table: "activos",
                columns: new[] { "empresa_id", "numero" },
                unique: true,
                filter: "NOT carga_anulada");

            migrationBuilder.AddCheckConstraint(
                name: "ck_activo_tasas",
                schema: "activos_fijos",
                table: "activos",
                sql: "(tasa_contable_anual IS NULL OR tasa_contable_anual BETWEEN 0 AND 100) AND (tasa_fiscal_anual IS NULL OR tasa_fiscal_anual BETWEEN 0 AND 100)");

            migrationBuilder.CreateIndex(
                name: "ix_capturas_anteriores_conteo_partida_id",
                schema: "activos_fijos",
                table: "capturas_anteriores_conteo",
                column: "partida_id");

            migrationBuilder.CreateIndex(
                name: "ix_conciliaciones_empresa_id_periodo_sucursal_id_huella",
                schema: "activos_fijos",
                table: "conciliaciones",
                columns: new[] { "empresa_id", "periodo", "sucursal_id", "huella" });

            migrationBuilder.CreateIndex(
                name: "ix_equivalencias_sap_empresa_id_clase_sap",
                schema: "activos_fijos",
                table: "equivalencias_sap",
                columns: new[] { "empresa_id", "clase_sap" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_impresiones_etiqueta_activo_id",
                schema: "activos_fijos",
                table: "impresiones_etiqueta",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "ix_versiones_clase_clase_id",
                schema: "activos_fijos",
                table: "versiones_clase",
                column: "clase_id");

            migrationBuilder.CreateIndex(
                name: "ix_versiones_clase_empresa_id_clase_id",
                schema: "activos_fijos",
                table: "versiones_clase",
                columns: new[] { "empresa_id", "clase_id" },
                unique: true,
                filter: "estado = 0");

            migrationBuilder.CreateIndex(
                name: "ix_versiones_clase_empresa_id_clase_id_numero",
                schema: "activos_fijos",
                table: "versiones_clase",
                columns: new[] { "empresa_id", "clase_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capturas_anteriores_conteo",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "conciliaciones",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "equivalencias_sap",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "impresiones_etiqueta",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "versiones_clase",
                schema: "activos_fijos");

            migrationBuilder.DropIndex(
                name: "ix_cargas_libro_empresa_id_huella_version_archivo",
                schema: "activos_fijos",
                table: "cargas_libro");

            migrationBuilder.DropIndex(
                name: "ix_activos_empresa_id_numero",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_activo_tasas",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropColumn(
                name: "corregido",
                schema: "activos_fijos",
                table: "traslados");

            migrationBuilder.DropColumn(
                name: "corrige_a",
                schema: "activos_fijos",
                table: "traslados");

            migrationBuilder.DropColumn(
                name: "capturado_en",
                schema: "activos_fijos",
                table: "partidas_conteo");

            migrationBuilder.DropColumn(
                name: "motivo_recaptura",
                schema: "activos_fijos",
                table: "partidas_conteo");

            migrationBuilder.DropColumn(
                name: "motivo_rechazo",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "rechazado_en",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "rechazado_por",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "revierte_a",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "venta_cancelada",
                schema: "activos_fijos",
                table: "operaciones_libro");

            migrationBuilder.DropColumn(
                name: "anulada",
                schema: "activos_fijos",
                table: "cargas_libro");

            migrationBuilder.DropColumn(
                name: "motivo_anulacion",
                schema: "activos_fijos",
                table: "cargas_libro");

            migrationBuilder.DropColumn(
                name: "documento_nuevo_id",
                schema: "activos_fijos",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "motivo_resolucion",
                schema: "activos_fijos",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "resolucion",
                schema: "activos_fijos",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "resuelto_en",
                schema: "activos_fijos",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "resuelto_por",
                schema: "activos_fijos",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "soporte_resolucion",
                schema: "activos_fijos",
                table: "avisos");

            migrationBuilder.DropColumn(
                name: "capitalizado_por",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropColumn(
                name: "carga_anulada",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropColumn(
                name: "tasa_contable_anual",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropColumn(
                name: "tasa_fiscal_anual",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.CreateIndex(
                name: "ix_cargas_libro_empresa_id_huella_version_archivo",
                schema: "activos_fijos",
                table: "cargas_libro",
                columns: new[] { "empresa_id", "huella", "version_archivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activos_empresa_id_numero",
                schema: "activos_fijos",
                table: "activos",
                columns: new[] { "empresa_id", "numero" },
                unique: true);
        }
    }
}
