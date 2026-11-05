using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class G10RecepcionRechazoAcuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acuse_recibe_at",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acuse_recibe_nombre",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_urgencia",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recibido_por_nombre",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidencia_blob_ref",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recepcion_rechazos",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_oc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidencia_blob_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepcion_rechazos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recepcion_rechazos_recepcion_id",
                schema: "almacen",
                table: "recepcion_rechazos",
                column: "recepcion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recepcion_rechazos",
                schema: "almacen");

            migrationBuilder.DropColumn(
                name: "acuse_recibe_at",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "acuse_recibe_nombre",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "motivo_urgencia",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "recibido_por_nombre",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "evidencia_blob_ref",
                schema: "almacen",
                table: "lineas_movimiento");
        }
    }
}
