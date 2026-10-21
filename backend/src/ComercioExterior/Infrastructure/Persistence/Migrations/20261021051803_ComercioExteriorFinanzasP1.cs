using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorFinanzasP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "financiero_cerrado",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "revision_financiera",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "anticipos_agente",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedimento = table.Column<string>(type: "text", nullable: false),
                    iva = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    igi = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    prv = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    referencia_pago = table.Column<string>(type: "text", nullable: true),
                    compensacion_recibida = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    iva_aplicado = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    igi_aplicado = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    prv_aplicado = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    pedimento_pagado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comprobante_pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_comprobacion = table.Column<DateOnly>(type: "date", nullable: true),
                    resoluciones = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anticipos_agente", x => x.id);
                    table.ForeignKey(
                        name: "fk_anticipos_agente_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conceptos_gasto",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_importacion = table.Column<int>(type: "integer", nullable: false),
                    base_exportacion = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conceptos_gasto", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "distribuciones_gasto",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    revision_financiera = table.Column<int>(type: "integer", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    renglones = table.Column<string>(type: "jsonb", nullable: false),
                    partidas = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_distribuciones_gasto", x => x.id);
                    table.ForeignKey(
                        name: "fk_distribuciones_gasto_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partidas_costo",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contenedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    valor_aduana_mxn = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    peso_kg = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    pedimento = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partidas_costo", x => x.id);
                    table.ForeignKey(
                        name: "fk_partidas_costo_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gastos_expediente",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "text", nullable: false),
                    excluido_del_costo = table.Column<bool>(type: "boolean", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura = table.Column<string>(type: "text", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    moneda = table.Column<string>(type: "text", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    fuente_tipo_cambio = table.Column<string>(type: "text", nullable: false),
                    importe_mxn = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    fecha_factura = table.Column<DateOnly>(type: "date", nullable: false),
                    pedimento = table.Column<string>(type: "text", nullable: true),
                    cotizado = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    contenedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    importes_directos = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gastos_expediente", x => x.id);
                    table.ForeignKey(
                        name: "fk_gastos_expediente_conceptos_gasto_concepto_id",
                        column: x => x.concepto_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "conceptos_gasto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gastos_expediente_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anticipos_agente_empresa_id_agente_id",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                columns: new[] { "empresa_id", "agente_id" });

            migrationBuilder.CreateIndex(
                name: "ix_anticipos_agente_expediente_id",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_conceptos_gasto_empresa_id_codigo",
                schema: "comercio_exterior",
                table: "conceptos_gasto",
                columns: new[] { "empresa_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_distribuciones_gasto_expediente_id_numero_version",
                schema: "comercio_exterior",
                table: "distribuciones_gasto",
                columns: new[] { "expediente_id", "numero_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gastos_expediente_concepto_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                column: "concepto_id");

            migrationBuilder.CreateIndex(
                name: "ix_gastos_expediente_expediente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_partidas_costo_expediente_id",
                schema: "comercio_exterior",
                table: "partidas_costo",
                column: "expediente_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anticipos_agente",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "distribuciones_gasto",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "gastos_expediente",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "partidas_costo",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "conceptos_gasto",
                schema: "comercio_exterior");

            migrationBuilder.DropColumn(
                name: "financiero_cerrado",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "revision_financiera",
                schema: "comercio_exterior",
                table: "expedientes");
        }
    }
}
