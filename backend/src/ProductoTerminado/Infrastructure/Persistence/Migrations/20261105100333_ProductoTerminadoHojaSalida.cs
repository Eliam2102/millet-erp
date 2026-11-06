using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoHojaSalida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "en_ruta_at",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "salidas_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    foranea = table.Column<bool>(type: "boolean", nullable: false),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    distancia_km = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    peso_kg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    origen_codigo_postal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    origen_estado = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    destino_codigo_postal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    destino_estado = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emitida_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    carta_porte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    carta_porte_folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salidas_pt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "albaranes_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salida_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    emitido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albaranes_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_albaranes_pt_pedidos_pt_pedido_pt_id",
                        column: x => x.pedido_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "pedidos_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_albaranes_pt_salidas_pt_salida_pt_id",
                        column: x => x.salida_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "salidas_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "albaran_pt_piezas",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    albaran_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pieza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albaran_pt_piezas", x => x.id);
                    table.ForeignKey(
                        name: "fk_albaran_pt_piezas_albaranes_pt_albaran_pt_id",
                        column: x => x.albaran_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "albaranes_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_albaran_pt_piezas_piezas_pieza_id",
                        column: x => x.pieza_id,
                        principalSchema: "producto_terminado",
                        principalTable: "piezas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_albaran_pt_piezas_albaran_pt_id_pieza_id",
                schema: "producto_terminado",
                table: "albaran_pt_piezas",
                columns: new[] { "albaran_pt_id", "pieza_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_albaran_pt_piezas_pieza_id",
                schema: "producto_terminado",
                table: "albaran_pt_piezas",
                column: "pieza_id");

            migrationBuilder.CreateIndex(
                name: "ix_albaranes_pt_empresa_id_folio",
                schema: "producto_terminado",
                table: "albaranes_pt",
                columns: new[] { "empresa_id", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_albaranes_pt_pedido_pt_id",
                schema: "producto_terminado",
                table: "albaranes_pt",
                column: "pedido_pt_id");

            migrationBuilder.CreateIndex(
                name: "ix_albaranes_pt_salida_pt_id_pedido_pt_id",
                schema: "producto_terminado",
                table: "albaranes_pt",
                columns: new[] { "salida_pt_id", "pedido_pt_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salidas_pt_empresa_id_estado_creada_at",
                schema: "producto_terminado",
                table: "salidas_pt",
                columns: new[] { "empresa_id", "estado", "creada_at" });

            migrationBuilder.CreateIndex(
                name: "ix_salidas_pt_empresa_id_folio",
                schema: "producto_terminado",
                table: "salidas_pt",
                columns: new[] { "empresa_id", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "albaran_pt_piezas",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "albaranes_pt",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "salidas_pt",
                schema: "producto_terminado");

            migrationBuilder.DropColumn(
                name: "en_ruta_at",
                schema: "producto_terminado",
                table: "pedidos_pt");
        }
    }
}
