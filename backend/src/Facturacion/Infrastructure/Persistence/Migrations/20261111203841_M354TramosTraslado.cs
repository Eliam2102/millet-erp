using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M354TramosTraslado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tramos_traslado",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    modalidad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    origen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destino = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    distancia_km = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    origen_codigo_postal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    origen_estado = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    destino_codigo_postal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    destino_estado = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    fecha_salida = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    carta_porte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tercero_nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    tercero_rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    documento_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    documento_folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    documento_uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    documento_fecha = table.Column<DateOnly>(type: "date", nullable: true),
                    documento_registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    documento_registrado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tramos_traslado", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tramos_traslado_correcciones",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carta_porte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tramo_traslado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor_anterior = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    valor_nuevo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tramos_traslado_correcciones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tramos_traslado_carta_porte",
                schema: "facturacion",
                table: "tramos_traslado",
                column: "carta_porte_id",
                filter: "carta_porte_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_tramos_traslado_pedido_orden",
                schema: "facturacion",
                table: "tramos_traslado",
                columns: new[] { "pedido_facturable_id", "orden" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tramos_correcciones_carta_porte",
                schema: "facturacion",
                table: "tramos_traslado_correcciones",
                columns: new[] { "carta_porte_id", "en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tramos_traslado",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "tramos_traslado_correcciones",
                schema: "facturacion");
        }
    }
}
