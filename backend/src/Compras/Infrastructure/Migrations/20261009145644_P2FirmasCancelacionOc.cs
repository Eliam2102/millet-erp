using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P2FirmasCancelacionOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_estado",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.CreateTable(
                name: "oc_solicitudes_cancelacion",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_anterior = table.Column<short>(type: "smallint", nullable: false),
                    solicitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_solicitud = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo_cancelacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo_solicitud = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    resolutor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_resolucion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmada = table.Column<bool>(type: "boolean", nullable: true),
                    motivo_resolucion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oc_solicitudes_cancelacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_oc_solicitudes_cancelacion_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_estado",
                schema: "compras",
                table: "ordenes_compra",
                sql: "estado BETWEEN 0 AND 7");

            migrationBuilder.CreateIndex(
                name: "ix_oc_solicitudes_cancelacion_orden_compra_id",
                schema: "compras",
                table: "oc_solicitudes_cancelacion",
                column: "orden_compra_id",
                unique: true,
                filter: "fecha_resolucion IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oc_solicitudes_cancelacion",
                schema: "compras");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_estado",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_estado",
                schema: "compras",
                table: "ordenes_compra",
                sql: "estado BETWEEN 0 AND 6");
        }
    }
}
