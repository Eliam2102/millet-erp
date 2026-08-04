using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CartaPorte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carta_porte",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    destino = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    distancia_km = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carta_porte_previa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_salida = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_llegada_estimada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carta_porte", x => x.id);
                    table.ForeignKey(
                        name: "fk_carta_porte_comprobante_id",
                        column: x => x.id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operador",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    num_licencia = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operador", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehiculo",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    config_vehicular = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    anio_modelo = table.Column<int>(type: "integer", nullable: false),
                    tipo_permiso_sct = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    num_permiso_sct = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    aseguradora = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    poliza_seguro = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehiculo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carta_porte_mercancia",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    carta_porte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    bienes_transp = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    clave_unidad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    peso_en_kg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    material_peligroso = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carta_porte_mercancia", x => x.id);
                    table.ForeignKey(
                        name: "fk_carta_porte_mercancia_cartas_porte_carta_porte_id",
                        column: x => x.carta_porte_id,
                        principalSchema: "facturacion",
                        principalTable: "carta_porte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_carta_porte_previa",
                schema: "facturacion",
                table: "carta_porte",
                column: "carta_porte_previa_id",
                filter: "carta_porte_previa_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_carta_porte_mercancia_carta_porte_id",
                schema: "facturacion",
                table: "carta_porte_mercancia",
                column: "carta_porte_id");

            migrationBuilder.CreateIndex(
                name: "ix_operador_rfc",
                schema: "facturacion",
                table: "operador",
                columns: new[] { "empresa_id", "rfc" });

            migrationBuilder.CreateIndex(
                name: "ix_vehiculo_placa",
                schema: "facturacion",
                table: "vehiculo",
                columns: new[] { "empresa_id", "placa" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carta_porte_mercancia",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "operador",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "vehiculo",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "carta_porte",
                schema: "facturacion");
        }
    }
}
