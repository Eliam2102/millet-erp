using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IngestaAwSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "estado_origen",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "version_origen",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bandeja_excepcion_importacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    pedido_ref = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    motivo = table.Column<short>(type: "smallint", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resuelto = table.Column<bool>(type: "boolean", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bandeja_excepcion_importacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingesta_control",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    clave_natural = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    hash_contenido = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ultima_version_aplicada = table.Column<long>(type: "bigint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ultima_lectura_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingesta_control", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pedido_facturable_snapshot",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_crudo = table.Column<string>(type: "jsonb", nullable: false),
                    leido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido_facturable_snapshot", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bandeja_excepcion_resuelto",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion",
                columns: new[] { "empresa_id", "resuelto" });

            migrationBuilder.CreateIndex(
                name: "ix_ingesta_control_estado",
                schema: "facturacion",
                table: "ingesta_control",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_ingesta_control_origen_clave",
                schema: "facturacion",
                table: "ingesta_control",
                columns: new[] { "origen", "clave_natural" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedido_facturable_snapshot_pedido",
                schema: "facturacion",
                table: "pedido_facturable_snapshot",
                column: "pedido_facturable_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bandeja_excepcion_importacion",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "ingesta_control",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "pedido_facturable_snapshot",
                schema: "facturacion");

            migrationBuilder.DropColumn(
                name: "estado_origen",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "version_origen",
                schema: "facturacion",
                table: "pedido_facturable");
        }
    }
}
