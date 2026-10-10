using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4ReppPagosTesoreria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_bloqueo",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pago_bloqueado",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultimo_cambio_cxp",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "repp_pagos_proveedor",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repp_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repp_pagos_proveedor", x => x.id);
                    table.ForeignKey(
                        name: "fk_repp_pagos_proveedor_aplicacion_pago_proveedor_pago_id",
                        column: x => x.pago_id,
                        principalSchema: "tesoreria",
                        principalTable: "aplicacion_pago_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repp_pagos_proveedor_repp_proveedor_recibido_repp_id",
                        column: x => x.repp_id,
                        principalSchema: "tesoreria",
                        principalTable: "repp_proveedor_recibido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repp_pagos_proveedor_pago_id",
                schema: "tesoreria",
                table: "repp_pagos_proveedor",
                column: "pago_id");

            migrationBuilder.CreateIndex(
                name: "ix_repp_pagos_proveedor_repp_id_pago_id",
                schema: "tesoreria",
                table: "repp_pagos_proveedor",
                columns: new[] { "repp_id", "pago_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repp_pagos_proveedor",
                schema: "tesoreria");

            migrationBuilder.DropColumn(
                name: "motivo_bloqueo",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "pago_bloqueado",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "ultimo_cambio_cxp",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");
        }
    }
}
