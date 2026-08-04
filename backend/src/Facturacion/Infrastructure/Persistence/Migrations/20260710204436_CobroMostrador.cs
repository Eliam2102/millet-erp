using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CobroMostrador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cobro_mostrador",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_venta_id = table.Column<short>(type: "smallint", nullable: true),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha_cobro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_cobrador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobro_mostrador", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobro_mostrador_caja_sesion_caja_sesion_id",
                        column: x => x.caja_sesion_id,
                        principalSchema: "facturacion",
                        principalTable: "caja_sesion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobro_mostrador_comprobante_comprobante_id",
                        column: x => x.comprobante_id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cobro_mostrador_forma_pago",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cobro_mostrador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forma_pago = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cuenta_ordenante = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cuenta_beneficiaria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobro_mostrador_forma_pago", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobro_mostrador_forma_pago_cobros_mostrador_cobro_mostrador",
                        column: x => x.cobro_mostrador_id,
                        principalSchema: "facturacion",
                        principalTable: "cobro_mostrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobro_mostrador_sesion",
                schema: "facturacion",
                table: "cobro_mostrador",
                column: "caja_sesion_id");

            migrationBuilder.CreateIndex(
                name: "ux_cobro_mostrador_comprobante_vigente",
                schema: "facturacion",
                table: "cobro_mostrador",
                column: "comprobante_id",
                unique: true,
                filter: "estado = 1");

            migrationBuilder.CreateIndex(
                name: "ix_cobro_forma_pago_cobro",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago",
                column: "cobro_mostrador_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobro_mostrador_forma_pago",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "cobro_mostrador",
                schema: "facturacion");
        }
    }
}
