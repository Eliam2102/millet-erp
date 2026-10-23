using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF01Nucleo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "activos_fijos");

            migrationBuilder.CreateTable(
                name: "clases",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cuenta_activo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_depreciacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_gasto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_puente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tasa_fiscal_anual = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    metodo = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("pk_clases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facturas_conciliadas",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_oc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "text", nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas_conciliadas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "secuencias",
                schema: "activos_fijos",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ultimo_numero = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secuencias", x => new { x.empresa_id, x.clase_id });
                });

            migrationBuilder.CreateTable(
                name: "activos",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    clase_id = table.Column<Guid>(type: "uuid", nullable: false),
                    marca = table.Column<string>(type: "text", nullable: true),
                    modelo = table.Column<string>(type: "text", nullable: true),
                    numero_serie = table.Column<string>(type: "text", nullable: true),
                    fecha_adquisicion = table.Column<DateOnly>(type: "date", nullable: false),
                    inicio_uso = table.Column<DateOnly>(type: "date", nullable: true),
                    valor_original = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    valor_moneda_original = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "text", nullable: false),
                    tipo_cambio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    depreciacion_acumulada = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    valor_actual = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion = table.Column<string>(type: "text", nullable: false),
                    centro_costo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    importacion = table.Column<string>(type: "text", nullable: false),
                    pedimento = table.Column<string>(type: "text", nullable: true),
                    arrendado = table.Column<bool>(type: "boolean", nullable: false),
                    documento_soporte = table.Column<string>(type: "text", nullable: true),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linea_oc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unidad_recepcion = table.Column<int>(type: "integer", nullable: false),
                    linea_factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    poliza_alta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activos", x => x.id);
                    table.ForeignKey(
                        name: "fk_activos_clases_clase_id",
                        column: x => x.clase_id,
                        principalSchema: "activos_fijos",
                        principalTable: "clases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimientos_valor",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_valor", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimientos_valor_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "traslados",
                schema: "activos_fijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen_responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen_sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen_ubicacion = table.Column<string>(type: "text", nullable: false),
                    origen_centro_costo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen_equipo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destino_responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino_sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino_ubicacion = table.Column<string>(type: "text", nullable: false),
                    destino_centro_costo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino_equipo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aceptado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_traslados", x => x.id);
                    table.ForeignKey(
                        name: "fk_traslados_activos_activo_id",
                        column: x => x.activo_id,
                        principalSchema: "activos_fijos",
                        principalTable: "activos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "activos_fijos",
                table: "clases",
                columns: new[] { "id", "activa", "clave", "created_at", "created_by", "cuenta_activo_id", "cuenta_depreciacion_id", "cuenta_gasto_id", "cuenta_puente_id", "deleted_at", "empresa_id", "metodo", "nombre", "tasa_fiscal_anual", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("0000000f-0002-0000-0000-000000000001"), true, "EDI", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "Edificios · propuesta VILO", 5m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0002-0000-0000-000000000002"), true, "MAQ", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "Maquinaria y equipo industrial · propuesta VILO", 10m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0002-0000-0000-000000000003"), true, "MOB", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "Mobiliario y equipo de oficina · propuesta VILO", 10m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0002-0000-0000-000000000004"), true, "TRA", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "Automóviles y equipo de transporte · propuesta VILO", 25m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0002-0000-0000-000000000005"), true, "COM", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "Equipo de cómputo · propuesta VILO", 30m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("0000000f-0002-0000-0000-000000000006"), true, "HER", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, null, null, null, new Guid("00000003-0000-0000-0000-000000000001"), "LineaRecta", "Herramientas y moldes · propuesta VILO", 35m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_activos_clase_id",
                schema: "activos_fijos",
                table: "activos",
                column: "clase_id");

            migrationBuilder.CreateIndex(
                name: "ix_activos_empresa_id_numero",
                schema: "activos_fijos",
                table: "activos",
                columns: new[] { "empresa_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activos_empresa_id_recepcion_id_linea_oc_id_unidad_recepcion",
                schema: "activos_fijos",
                table: "activos",
                columns: new[] { "empresa_id", "recepcion_id", "linea_oc_id", "unidad_recepcion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activos_empresa_id_sucursal_id_estado",
                schema: "activos_fijos",
                table: "activos",
                columns: new[] { "empresa_id", "sucursal_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_clases_empresa_id_clave",
                schema: "activos_fijos",
                table: "clases",
                columns: new[] { "empresa_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturas_conciliadas_empresa_id_linea_factura_id",
                schema: "activos_fijos",
                table: "facturas_conciliadas",
                columns: new[] { "empresa_id", "linea_factura_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_valor_activo_id_documento_id",
                schema: "activos_fijos",
                table: "movimientos_valor",
                columns: new[] { "activo_id", "documento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_traslados_activo_id",
                schema: "activos_fijos",
                table: "traslados",
                column: "activo_id",
                unique: true,
                filter: "aceptado_en IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facturas_conciliadas",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "movimientos_valor",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "secuencias",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "traslados",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "activos",
                schema: "activos_fijos");

            migrationBuilder.DropTable(
                name: "clases",
                schema: "activos_fijos");
        }
    }
}
