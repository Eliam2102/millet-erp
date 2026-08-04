using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacionVentaActivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "autorizacion_venta_activo",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    valor_en_libros = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    depreciacion_acumulada = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    es_importacion = table.Column<bool>(type: "boolean", nullable: false),
                    precio_venta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    autorizado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_autorizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_venta_activo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_venta_activo_estado",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                columns: new[] { "empresa_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizacion_venta_activo",
                schema: "facturacion");
        }
    }
}
