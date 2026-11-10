using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class M047LotesCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lotes_carga",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fecha_corte = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    mapeo_json = table.Column<string>(type: "jsonb", nullable: false),
                    parametros_json = table.Column<string>(type: "jsonb", nullable: false),
                    filas_json = table.Column<string>(type: "jsonb", nullable: false),
                    validacion_json = table.Column<string>(type: "jsonb", nullable: false),
                    puede_aplicar = table.Column<bool>(type: "boolean", nullable: false),
                    total_filas = table.Column<int>(type: "integer", nullable: false),
                    total_errores = table.Column<int>(type: "integer", nullable: false),
                    subido_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subido_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decidido_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decidido_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decidido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    aplicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    folio_resultado = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    historial_json = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotes_carga", x => x.id);
                    table.CheckConstraint("ck_lotes_carga_estado", "estado BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_lotes_carga_otra_persona", "estado NOT IN (2, 4) OR decidido_por_id <> subido_por_id");
                    table.CheckConstraint("ck_lotes_carga_rechazo_con_motivo", "estado <> 3 OR (motivo_rechazo IS NOT NULL AND char_length(motivo_rechazo) >= 5)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lotes_carga_empresa_tipo_huella",
                schema: "compartido",
                table: "lotes_carga",
                columns: new[] { "empresa_id", "tipo", "huella" });

            migrationBuilder.CreateIndex(
                name: "ix_lotes_carga_empresa_tipo_subido",
                schema: "compartido",
                table: "lotes_carga",
                columns: new[] { "empresa_id", "tipo", "subido_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lotes_carga",
                schema: "compartido");
        }
    }
}
