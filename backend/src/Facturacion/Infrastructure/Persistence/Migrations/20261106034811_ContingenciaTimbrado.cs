using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContingenciaTimbrado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contingencia_timbrado",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    error_codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ocurrida_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_limite = table.Column<DateOnly>(type: "date", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comprobante_folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    registrada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registrada_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cerrada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uuid_final = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    nota_cierre = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cerrada_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    alerta_enviada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contingencia_timbrado", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contingencia_timbrado_estado_limite",
                schema: "facturacion",
                table: "contingencia_timbrado",
                columns: new[] { "estado", "fecha_limite" });

            migrationBuilder.CreateIndex(
                name: "ix_contingencia_timbrado_ocurrida",
                schema: "facturacion",
                table: "contingencia_timbrado",
                column: "ocurrida_at");

            migrationBuilder.CreateIndex(
                name: "ux_contingencia_timbrado_comprobante_abierta",
                schema: "facturacion",
                table: "contingencia_timbrado",
                column: "comprobante_id",
                unique: true,
                filter: "comprobante_id IS NOT NULL AND estado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contingencia_timbrado",
                schema: "facturacion");
        }
    }
}
