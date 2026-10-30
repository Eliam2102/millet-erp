using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoEsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "producto_terminado");

            migrationBuilder.CreateTable(
                name: "escaneos",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "text", nullable: false),
                    origen_evento_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    numero_pedido = table.Column<string>(type: "text", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    numero_pieza = table.Column<int>(type: "integer", nullable: true),
                    sucursal_clave_aw = table.Column<string>(type: "text", nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escaneado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    codigo_barras = table.Column<string>(type: "text", nullable: true),
                    usuario_aw = table.Column<string>(type: "text", nullable: true),
                    estatus_aw = table.Column<int>(type: "integer", nullable: false),
                    resultado = table.Column<string>(type: "text", nullable: false),
                    motivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    pieza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_escaneos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    integration_empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sucursal_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condicion_pago_origen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    version_origen = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_pt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reader_watermarks",
                schema: "producto_terminado",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    barcode = table.Column<string>(type: "text", nullable: false),
                    station = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reader_watermarks", x => new { x.empresa_id, x.origin });
                });

            migrationBuilder.CreateTable(
                name: "pedido_pt_posiciones",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    piezas_esperadas = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido_pt_posiciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedido_pt_posiciones_pedidos_pt_pedido_pt_id",
                        column: x => x.pedido_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "pedidos_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "piezas",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    numero_pieza = table.Column<int>(type: "integer", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    escaneada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estatus_aw = table.Column<int>(type: "integer", nullable: false),
                    escaneo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_piezas", x => x.id);
                    table.ForeignKey(
                        name: "fk_piezas_pedidos_pt_pedido_pt_id",
                        column: x => x.pedido_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "pedidos_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_pieza",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pieza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    sucursal_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sucursal_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_anterior = table.Column<string>(type: "text", nullable: true),
                    estado_nuevo = table.Column<string>(type: "text", nullable: false),
                    referencia = table.Column<Guid>(type: "uuid", nullable: false),
                    ocurrido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_pieza", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimientos_pieza_piezas_pieza_id",
                        column: x => x.pieza_id,
                        principalSchema: "producto_terminado",
                        principalTable: "piezas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_escaneos_empresa_id_last_attempt_at",
                schema: "producto_terminado",
                table: "escaneos",
                columns: new[] { "empresa_id", "last_attempt_at" },
                filter: "resultado IN ('Pospuesto','Excepcion')");

            migrationBuilder.CreateIndex(
                name: "ix_escaneos_empresa_id_origen_origen_evento_id",
                schema: "producto_terminado",
                table: "escaneos",
                columns: new[] { "empresa_id", "origen", "origen_evento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_pieza_pieza_id_tipo_referencia",
                schema: "producto_terminado",
                table: "movimientos_pieza",
                columns: new[] { "pieza_id", "tipo", "referencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_integration_empresa_id",
                schema: "producto_terminado",
                table: "outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "producto_terminado",
                table: "outbox",
                column: "published_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_pt_posiciones_pedido_pt_id_posicion",
                schema: "producto_terminado",
                table: "pedido_pt_posiciones",
                columns: new[] { "pedido_pt_id", "posicion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_pt_empresa_id_numero_pedido",
                schema: "producto_terminado",
                table: "pedidos_pt",
                columns: new[] { "empresa_id", "numero_pedido" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_pt_empresa_id_sucursal_venta_id",
                schema: "producto_terminado",
                table: "pedidos_pt",
                columns: new[] { "empresa_id", "sucursal_venta_id" });

            migrationBuilder.CreateIndex(
                name: "ix_piezas_empresa_id_sucursal_id_estado",
                schema: "producto_terminado",
                table: "piezas",
                columns: new[] { "empresa_id", "sucursal_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_piezas_pedido_pt_id_estado",
                schema: "producto_terminado",
                table: "piezas",
                columns: new[] { "pedido_pt_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_piezas_pedido_pt_id_posicion_numero_pieza",
                schema: "producto_terminado",
                table: "piezas",
                columns: new[] { "pedido_pt_id", "posicion", "numero_pieza" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escaneos",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "movimientos_pieza",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "pedido_pt_posiciones",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "reader_watermarks",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "piezas",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "pedidos_pt",
                schema: "producto_terminado");
        }
    }
}
