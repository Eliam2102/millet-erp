using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tesoreria");

            migrationBuilder.CreateTable(
                name: "concepto_movimiento",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    clasificacion_flujo = table.Column<short>(type: "smallint", nullable: false),
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
                    table.PrimaryKey("pk_concepto_movimiento", x => x.id);
                    table.CheckConstraint("ck_concepto_movimiento_clasificacion", "clasificacion_flujo IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "cuenta_bancaria",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banco = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    numero_cuenta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    clabe = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cuenta_contable_ref = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    perfil_extracto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuenta_bancaria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evento_procesado",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_tipo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evento_procesado", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_events_outbox",
                schema: "tesoreria",
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
                name: "pasivo_pendiente_pago",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monto_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    saldo_pendiente = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    uuid_cfdi = table.Column<Guid>(type: "uuid", nullable: true),
                    folio_proveedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    metodo_pago = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    recibido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pasivo_pendiente_pago", x => x.id);
                    table.CheckConstraint("ck_pasivo_pendiente_saldo_no_negativo", "saldo_pendiente >= 0");
                });

            migrationBuilder.CreateTable(
                name: "repp_proveedor_recibido",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid_complemento = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_complemento = table.Column<DateOnly>(type: "date", nullable: false),
                    xml_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repp_proveedor_recibido", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "corrida_pago",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    autorizada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    oficio_generado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago", x => x.id);
                    table.CheckConstraint("ck_corrida_pago_estado", "estado IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("ck_corrida_pago_total_no_negativo", "total >= 0");
                    table.ForeignKey(
                        name: "fk_corrida_pago_cuenta_bancaria_cuenta_bancaria_id",
                        column: x => x.cuenta_bancaria_id,
                        principalSchema: "tesoreria",
                        principalTable: "cuenta_bancaria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimiento_bancario",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sentido = table.Column<short>(type: "smallint", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha_valor = table.Column<DateOnly>(type: "date", nullable: false),
                    referencia_bancaria = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    concepto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_aplicacion = table.Column<short>(type: "smallint", nullable: false),
                    estado_conciliacion = table.Column<short>(type: "smallint", nullable: false),
                    beneficiario_tipo = table.Column<short>(type: "smallint", nullable: true),
                    beneficiario_ref = table.Column<Guid>(type: "uuid", nullable: true),
                    contramovimiento_de = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_no_aplicado = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimiento_bancario", x => x.id);
                    table.CheckConstraint("ck_movimiento_bancario_beneficiario_tipo", "beneficiario_tipo IS NULL OR beneficiario_tipo IN (1, 2, 3)");
                    table.CheckConstraint("ck_movimiento_bancario_estado_aplicacion", "estado_aplicacion IN (1, 2, 3)");
                    table.CheckConstraint("ck_movimiento_bancario_estado_conciliacion", "estado_conciliacion IN (1, 2)");
                    table.CheckConstraint("ck_movimiento_bancario_monto_positivo", "monto > 0");
                    table.CheckConstraint("ck_movimiento_bancario_sentido", "sentido IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_movimiento_bancario_concepto_movimiento_concepto_id",
                        column: x => x.concepto_id,
                        principalSchema: "tesoreria",
                        principalTable: "concepto_movimiento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_bancario_cuenta_bancaria_cuenta_bancaria_id",
                        column: x => x.cuenta_bancaria_id,
                        principalSchema: "tesoreria",
                        principalTable: "cuenta_bancaria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_bancario_movimiento_bancario_contramovimiento_de",
                        column: x => x.contramovimiento_de,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "corrida_pago_linea",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe_programado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ejecutada = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago_linea", x => x.id);
                    table.CheckConstraint("ck_corrida_pago_linea_importe_positivo", "importe_programado > 0");
                    table.ForeignKey(
                        name: "fk_corrida_pago_linea_corrida_pago_corrida_id",
                        column: x => x.corrida_id,
                        principalSchema: "tesoreria",
                        principalTable: "corrida_pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "aplicacion_pago_proveedor",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe_aplicado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    corrida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revertida = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aplicacion_pago_proveedor", x => x.id);
                    table.CheckConstraint("ck_aplicacion_pago_importe_positivo", "importe_aplicado > 0");
                    table.ForeignKey(
                        name: "fk_aplicacion_pago_proveedor_corridas_pago_corrida_id",
                        column: x => x.corrida_id,
                        principalSchema: "tesoreria",
                        principalTable: "corrida_pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_aplicacion_pago_proveedor_movimiento_bancario_movimiento_id",
                        column: x => x.movimiento_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposito_confirmacion",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    propuesta_cxc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    repp_timbrado = table.Column<bool>(type: "boolean", nullable: false),
                    facturas_json = table.Column<string>(type: "jsonb", nullable: false),
                    confirmada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposito_confirmacion", x => x.id);
                    table.CheckConstraint("ck_deposito_confirmacion_estado", "estado IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_deposito_confirmacion_movimiento_bancario_movimiento_id",
                        column: x => x.movimiento_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                columns: new[] { "id", "activo", "clasificacion_flujo", "created_at", "created_by", "deleted_at", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("0000000b-1001-0000-0000-000000000001"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Pago a proveedor", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000002"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cobro de cliente", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000003"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Depósito de caja", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000004"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Comisión bancaria", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000005"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Impuestos", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000006"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Nómina", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000007"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Traspaso entre cuentas propias", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000008"), true, (short)2, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Compra de activo fijo", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-000000000009"), true, (short)2, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Venta de activo fijo", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-00000000000a"), true, (short)3, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Intereses ganados", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-00000000000b"), true, (short)3, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Intereses y gastos financieros", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-00000000000c"), true, (short)3, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Disposición de crédito", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000b-1001-0000-0000-00000000000d"), true, (short)3, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Pago de crédito", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_aplicacion_pago_factura",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                column: "factura_proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_aplicacion_pago_proveedor_corrida_id",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                column: "corrida_id");

            migrationBuilder.CreateIndex(
                name: "ux_aplicacion_pago_movimiento_factura",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                columns: new[] { "movimiento_id", "factura_proveedor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_concepto_movimiento_nombre",
                schema: "tesoreria",
                table: "concepto_movimiento",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_corrida_pago_cuenta_bancaria_id",
                schema: "tesoreria",
                table: "corrida_pago",
                column: "cuenta_bancaria_id");

            migrationBuilder.CreateIndex(
                name: "ix_corrida_pago_empresa_estado",
                schema: "tesoreria",
                table: "corrida_pago",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_corrida_pago_linea_factura",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                columns: new[] { "corrida_id", "factura_proveedor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cuenta_bancaria_empresa_numero",
                schema: "tesoreria",
                table: "cuenta_bancaria",
                columns: new[] { "empresa_id", "numero_cuenta" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deposito_confirmacion_empresa_estado",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_deposito_confirmacion_movimiento_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "movimiento_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposito_confirmacion_propuesta",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "propuesta_cxc_id");

            migrationBuilder.CreateIndex(
                name: "ux_evento_procesado_id_tipo",
                schema: "tesoreria",
                table: "evento_procesado",
                columns: new[] { "evento_id", "evento_tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_integration_empresa_id",
                schema: "tesoreria",
                table: "integration_events_outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_pending",
                schema: "tesoreria",
                table: "integration_events_outbox",
                column: "published_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_bancario_concepto_id",
                schema: "tesoreria",
                table: "movimiento_bancario",
                column: "concepto_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_bancario_contramovimiento_de",
                schema: "tesoreria",
                table: "movimiento_bancario",
                column: "contramovimiento_de");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_bancario_cuenta_fecha",
                schema: "tesoreria",
                table: "movimiento_bancario",
                columns: new[] { "cuenta_bancaria_id", "fecha_valor" });

            migrationBuilder.CreateIndex(
                name: "ux_pago_cuenta_abierto",
                schema: "tesoreria",
                table: "movimiento_bancario",
                columns: new[] { "empresa_id", "beneficiario_ref" },
                unique: true,
                filter: "sentido = 2 AND estado_aplicacion = 1 AND beneficiario_tipo = 1 AND contramovimiento_de IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pasivo_pendiente_empresa_vencimiento",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                columns: new[] { "empresa_id", "fecha_vencimiento" });

            migrationBuilder.CreateIndex(
                name: "ix_pasivo_pendiente_proveedor",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ux_pasivo_pendiente_factura",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                column: "factura_proveedor_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repp_recibido_factura",
                schema: "tesoreria",
                table: "repp_proveedor_recibido",
                column: "factura_proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ux_repp_recibido_uuid",
                schema: "tesoreria",
                table: "repp_proveedor_recibido",
                column: "uuid_complemento",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aplicacion_pago_proveedor",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "corrida_pago_linea",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "deposito_confirmacion",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "evento_procesado",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "integration_events_outbox",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "pasivo_pendiente_pago",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "repp_proveedor_recibido",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "corrida_pago",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "movimiento_bancario",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "concepto_movimiento",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "cuenta_bancaria",
                schema: "tesoreria");
        }
    }
}
