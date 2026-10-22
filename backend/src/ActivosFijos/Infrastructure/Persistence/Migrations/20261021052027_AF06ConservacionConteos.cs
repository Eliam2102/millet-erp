using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF06ConservacionConteos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultima_impresion_en",
                schema: "activos_fijos",
                table: "activos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "avisos",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    es_mantenimiento = table.Column<bool>(type: "boolean", nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_aviso = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conteos",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    abierto_por = table.Column<Guid>(type: "uuid", nullable: false),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conteos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documentos",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    adjunto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    emisor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fecha_emision = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: true),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    notas = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reemplaza_a = table.Column<Guid>(type: "uuid", nullable: true),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    reemplazado = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentos_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mantenimientos",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    costo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proximo = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mantenimientos", x => x.id);
                    table.ForeignKey(
                        name: "fk_mantenimientos_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partidas_conteo",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    numero = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    descripcion_esperada = table.Column<string>(type: "text", nullable: false),
                    ubicacion_esperada = table.Column<string>(type: "text", nullable: true),
                    resultado = table.Column<string>(type: "text", nullable: false),
                    ubicacion_real = table.Column<string>(type: "text", nullable: true),
                    evidencia = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidencia_autorizacion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    propuesta = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partidas_conteo", x => x.id);
                    table.ForeignKey(
                        name: "fk_partidas_conteo_conteos_conteo_id",
                        column: x => x.conteo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "conteos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_activo_id",
                schema: "activos_fijos",
                table: "avisos",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_empresa_id_fuente_id_tipo",
                schema: "activos_fijos",
                table: "avisos",
                columns: new[] { "empresa_id", "fuente_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conteos_empresa_id_sucursal_id",
                schema: "activos_fijos",
                table: "conteos",
                columns: new[] { "empresa_id", "sucursal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_activo_id",
                schema: "activos_fijos",
                table: "documentos",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_adjunto_id",
                schema: "activos_fijos",
                table: "documentos",
                column: "adjunto_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_reemplaza_a",
                schema: "activos_fijos",
                table: "documentos",
                column: "reemplaza_a",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mantenimientos_activo_id",
                schema: "activos_fijos",
                table: "mantenimientos",
                column: "activo_id");

            migrationBuilder.CreateIndex(
                name: "ix_partidas_conteo_conteo_id_numero",
                schema: "activos_fijos",
                table: "partidas_conteo",
                columns: new[] { "conteo_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "documentos",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "mantenimientos",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "partidas_conteo",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "conteos",
                schema: "activos_fijos");

            migrationBuilder.DropColumn(
                name: "ultima_impresion_en",
                schema: "activos_fijos",
                table: "activos");
        }
    }
}
