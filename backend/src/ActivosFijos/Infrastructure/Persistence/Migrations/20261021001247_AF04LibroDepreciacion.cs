using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF04LibroDepreciacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "acumulada_carga",
                schema: "activos_fijos",
                table: "activos",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "corte_carga",
                schema: "activos_fijos",
                table: "activos",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_baja",
                schema: "activos_fijos",
                table: "activos",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cargas_libro",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    huella = table.Column<string>(type: "text", nullable: false),
                    version_archivo = table.Column<string>(type: "text", nullable: false),
                    corte = table.Column<DateOnly>(type: "date", nullable: false),
                    cuadre_json = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cargas_libro", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "configuracion_libro",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revalorizacion_habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    cuenta_utilidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_perdida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_venta_puente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_revalorizacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_libro", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "corridas",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<DateOnly>(type: "date", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anulada = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_anulacion = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corridas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operaciones_libro",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: false),
                    evidencia = table.Column<string>(type: "text", nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    autorizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anulada = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operaciones_libro", x => x.id);
                    table.ForeignKey(
                        name: "fk_operaciones_libro_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "depreciaciones",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "text", nullable: false),
                    clase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    centro_costo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    @base = table.Column<decimal>(name: "base", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tasa = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    acumulada_antes = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_depreciaciones_corridas_corrida_id",
                        column: x => x.corrida_id,
                        principalSchema: "activos_fijos",
                        principalTable: "corridas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cargas_libro_empresa_id_huella_version_archivo",
                schema: "activos_fijos",
                table: "cargas_libro",
                columns: new[] { "empresa_id", "huella", "version_archivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_libro_empresa_id",
                schema: "activos_fijos",
                table: "configuracion_libro",
                column: "empresa_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_corridas_empresa_id_periodo",
                schema: "activos_fijos",
                table: "corridas",
                columns: new[] { "empresa_id", "periodo" },
                unique: true,
                filter: "NOT anulada");

            migrationBuilder.CreateIndex(
                name: "ix_corridas_empresa_id_periodo_revision",
                schema: "activos_fijos",
                table: "corridas",
                columns: new[] { "empresa_id", "periodo", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_depreciaciones_corrida_id_activo_id",
                schema: "activos_fijos",
                table: "depreciaciones",
                columns: new[] { "corrida_id", "activo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operaciones_libro_activo_id",
                schema: "activos_fijos",
                table: "operaciones_libro",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "ix_operaciones_libro_empresa_id_factura_id",
                schema: "activos_fijos",
                table: "operaciones_libro",
                columns: new[] { "empresa_id", "factura_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cargas_libro",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "configuracion_libro",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "depreciaciones",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "operaciones_libro",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "corridas",
                schema: "activos_fijos");

            migrationBuilder.DropColumn(
                name: "acumulada_carga",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropColumn(
                name: "corte_carga",
                schema: "activos_fijos",
                table: "activos");

            migrationBuilder.DropColumn(
                name: "fecha_baja",
                schema: "activos_fijos",
                table: "activos");
        }
    }
}
