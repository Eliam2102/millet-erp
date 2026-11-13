using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6ProductoTerminadoSalidas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "almacen",
                schema: "producto_terminado",
                table: "piezas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "costo",
                schema: "producto_terminado",
                table: "piezas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lugar",
                schema: "producto_terminado",
                table: "piezas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ubicada_at",
                schema: "producto_terminado",
                table: "piezas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ubicada_por_id",
                schema: "producto_terminado",
                table: "piezas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "correcciones",
                schema: "producto_terminado",
                table: "entregas_pt",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelacion_rechazada_at",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelacion_rechazada_por_id",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelacion_solicitada_at",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelacion_solicitada_por_id",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_rechazo_cancelacion",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_solicitud_cancelacion",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "casos_dano_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    etapa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidencia = table.Column<byte[]>(type: "bytea", nullable: true),
                    registrado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dictamen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    observacion_dictamen = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pedido_refabricacion_aw = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    dictaminado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dictaminado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correctivo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    documento_fiscal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    documento_fiscal_folio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cerrado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cerrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_casos_dano_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_casos_dano_pt_pedidos_pt_pedido_pt_id",
                        column: x => x.pedido_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "pedidos_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entrega_pt_versiones",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receptor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entregadas = table.Column<int>(type: "integer", nullable: false),
                    rechazadas = table.Column<int>(type: "integer", nullable: false),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    firma = table.Column<byte[]>(type: "bytea", nullable: true),
                    foto = table.Column<byte[]>(type: "bytea", nullable: true),
                    entregada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    motivo_correccion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    corregida_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    corregida_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entrega_pt_versiones", x => x.id);
                    table.ForeignKey(
                        name: "fk_entrega_pt_versiones_entregas_pt_entrega_pt_id",
                        column: x => x.entrega_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "entregas_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solicitudes_salida_parcial_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pieza_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    solicitada_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nivel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    suplencia = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    motivo_resolucion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resuelta_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_salida_parcial_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitudes_salida_parcial_pt_pedidos_pt_pedido_pt_id",
                        column: x => x.pedido_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "pedidos_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "caso_dano_pt_piezas",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caso_dano_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pieza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caso_dano_pt_piezas", x => x.id);
                    table.ForeignKey(
                        name: "fk_caso_dano_pt_piezas_casos_dano_pt_caso_dano_pt_id",
                        column: x => x.caso_dano_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "casos_dano_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_caso_dano_pt_piezas_piezas_pieza_id",
                        column: x => x.pieza_id,
                        principalSchema: "producto_terminado",
                        principalTable: "piezas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_caso_dano_pt_piezas_caso_dano_pt_id_pieza_id",
                schema: "producto_terminado",
                table: "caso_dano_pt_piezas",
                columns: new[] { "caso_dano_pt_id", "pieza_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caso_dano_pt_piezas_pieza_id",
                schema: "producto_terminado",
                table: "caso_dano_pt_piezas",
                column: "pieza_id");

            migrationBuilder.CreateIndex(
                name: "ix_casos_dano_pt_empresa_id_estado_registrado_at",
                schema: "producto_terminado",
                table: "casos_dano_pt",
                columns: new[] { "empresa_id", "estado", "registrado_at" });

            migrationBuilder.CreateIndex(
                name: "ix_casos_dano_pt_empresa_id_folio",
                schema: "producto_terminado",
                table: "casos_dano_pt",
                columns: new[] { "empresa_id", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_casos_dano_pt_pedido_pt_id",
                schema: "producto_terminado",
                table: "casos_dano_pt",
                column: "pedido_pt_id");

            migrationBuilder.CreateIndex(
                name: "ix_entrega_pt_versiones_entrega_pt_id_numero",
                schema: "producto_terminado",
                table: "entrega_pt_versiones",
                columns: new[] { "entrega_pt_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_salida_parcial_pt_pedido_pt_id_estado",
                schema: "producto_terminado",
                table: "solicitudes_salida_parcial_pt",
                columns: new[] { "pedido_pt_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_pt_parcial_pendiente_por_pedido",
                schema: "producto_terminado",
                table: "solicitudes_salida_parcial_pt",
                column: "pedido_pt_id",
                unique: true,
                filter: "estado = 'Pendiente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caso_dano_pt_piezas",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "entrega_pt_versiones",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "solicitudes_salida_parcial_pt",
                schema: "producto_terminado");

            migrationBuilder.DropTable(
                name: "casos_dano_pt",
                schema: "producto_terminado");

            migrationBuilder.DropColumn(
                name: "almacen",
                schema: "producto_terminado",
                table: "piezas");

            migrationBuilder.DropColumn(
                name: "costo",
                schema: "producto_terminado",
                table: "piezas");

            migrationBuilder.DropColumn(
                name: "lugar",
                schema: "producto_terminado",
                table: "piezas");

            migrationBuilder.DropColumn(
                name: "ubicada_at",
                schema: "producto_terminado",
                table: "piezas");

            migrationBuilder.DropColumn(
                name: "ubicada_por_id",
                schema: "producto_terminado",
                table: "piezas");

            migrationBuilder.DropColumn(
                name: "correcciones",
                schema: "producto_terminado",
                table: "entregas_pt");

            migrationBuilder.DropColumn(
                name: "cancelacion_rechazada_at",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "cancelacion_rechazada_por_id",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "cancelacion_solicitada_at",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "cancelacion_solicitada_por_id",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "motivo_rechazo_cancelacion",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "motivo_solicitud_cancelacion",
                schema: "producto_terminado",
                table: "albaranes_pt");
        }
    }
}
