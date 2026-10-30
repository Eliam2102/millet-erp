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
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: true),
                    u_pos = table.Column<int>(type: "integer", nullable: true),
                    numero_pieza_texto = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    estacion = table.Column<int>(type: "integer", nullable: false),
                    area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    registrado_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    registrado_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recibido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clave_sucursal_aw = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    pieza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_escaneos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_events_outbox",
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
                    table.PrimaryKey("pk_integration_events_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sucursal_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_entrega_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                name: "pedido_pt_posiciones",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    piezas_esperadas = table.Column<int>(type: "integer", nullable: false),
                    cantidad_no_entera = table.Column<bool>(type: "boolean", nullable: false),
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
                    etiqueta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    escaneada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sucursal_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sucursal_destino_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_anterior = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    estado_nuevo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    referencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ocurrido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                name: "ix_pt_escaneos_abiertos",
                schema: "producto_terminado",
                table: "escaneos",
                column: "resultado",
                filter: "resultado IN ('Pospuesto','Excepcion','Reposicion')");

            migrationBuilder.CreateIndex(
                name: "ix_pt_escaneos_marca_agua",
                schema: "producto_terminado",
                table: "escaneos",
                columns: new[] { "empresa_id", "origen", "registrado_at" });

            migrationBuilder.CreateIndex(
                name: "ux_pt_escaneo_origen_etiqueta_hora",
                schema: "producto_terminado",
                table: "escaneos",
                columns: new[] { "empresa_id", "origen", "etiqueta", "registrado_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_integration_empresa_id",
                schema: "producto_terminado",
                table: "integration_events_outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_pending",
                schema: "producto_terminado",
                table: "integration_events_outbox",
                column: "published_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_pieza_pieza_id_tipo_referencia",
                schema: "producto_terminado",
                table: "movimientos_pieza",
                columns: new[] { "pieza_id", "tipo", "referencia" },
                unique: true);

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
                name: "ix_piezas_empresa_id_etiqueta",
                schema: "producto_terminado",
                table: "piezas",
                columns: new[] { "empresa_id", "etiqueta" },
                unique: true);

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
                name: "integration_events_outbox",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "movimientos_pieza",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "pedido_pt_posiciones",
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
