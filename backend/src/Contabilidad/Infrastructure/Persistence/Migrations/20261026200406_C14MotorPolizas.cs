using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C14MotorPolizas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plantillas_contables",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo_operacion_clave = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    concepto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lineas = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantillas_contables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rechazos_contables",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    causa = table.Column<short>(type: "smallint", nullable: false),
                    detalle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    mensaje = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_descarte = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rechazos_contables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles_cuenta",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles_cuenta", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_cuenta_cuentas_contables_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "cuentas_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles_cuenta_cambios",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_anterior_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_nueva_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cambiado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles_cuenta_cambios", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_cuenta_cambios_roles_cuenta_rol_cuenta_id",
                        column: x => x.rol_cuenta_id,
                        principalSchema: "contabilidad",
                        principalTable: "roles_cuenta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_plantillas_contables_version",
                schema: "contabilidad",
                table: "plantillas_contables",
                columns: new[] { "empresa_id", "tipo_evento", "numero_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rechazos_contables_bandeja",
                schema: "contabilidad",
                table: "rechazos_contables",
                columns: new[] { "empresa_id", "estado", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ux_rechazos_contables_evento",
                schema: "contabilidad",
                table: "rechazos_contables",
                columns: new[] { "empresa_id", "tipo_evento", "evento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_cuenta_cuenta_id",
                schema: "contabilidad",
                table: "roles_cuenta",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ux_roles_cuenta_codigo_filtro",
                schema: "contabilidad",
                table: "roles_cuenta",
                columns: new[] { "empresa_id", "codigo", "sucursal_id", "moneda", "vigente_desde" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_roles_cuenta_cambios_rol",
                schema: "contabilidad",
                table: "roles_cuenta_cambios",
                columns: new[] { "rol_cuenta_id", "cambiado_en" });

            // Siembra C1.4: roles sin cuenta (Laura los mapea; V12) y la plantilla #2.
            migrationBuilder.Sql("""
                -- C1.4: roles del plano C1 §4 y de cobro (23-oct), SIN cuenta: Laura los mapea en «Roles de cuenta» (V12 pendiente);
                -- mientras, el motor rechaza con RolSinCuenta (nada se pierde).
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C14', 'migration-C14', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('INVENTARIO', 'Inventario de almacén (entrada valorada)'),
                    ('MERCANCIA_RECIBIDA_POR_FACTURAR', 'Cuenta puente: mercancía recibida por facturar (K10.5; E7: también capitalización de activos)'),
                    ('GASTO', 'Gasto por concepto y centro de costo (líneas de servicio, E6)'),
                    ('IVA_ACREDITABLE_PENDIENTE', 'IVA acreditable pendiente de pago'),
                    ('PROVEEDORES', 'Proveedores (cuenta colectiva; auxiliar = proveedor)'),
                    ('RETENCIONES_POR_PAGAR', 'Retenciones de impuestos por pagar'),
                    ('ANTICIPO_A_PROVEEDORES', 'Anticipos a proveedores'),
                    ('BANCOS', 'Bancos'),
                    ('IVA_ACREDITABLE_PAGADO', 'IVA acreditable pagado'),
                    ('CLIENTES', 'Clientes (cuenta colectiva; auxiliar = cliente)'),
                    ('VENTAS', 'Ventas'),
                    ('IVA_TRASLADADO_NO_COBRADO', 'IVA trasladado no cobrado'),
                    ('ANTICIPOS_DE_CLIENTES', 'Anticipos de clientes'),
                    ('DESCUENTOS_SOBRE_VENTAS', 'Descuentos y bonificaciones sobre ventas'),
                    ('CAJA', 'Caja'),
                    ('AJUSTE_INVENTARIO', 'Ajustes de inventario'),
                    ('MERMA', 'Merma'),
                    ('CONSUMO', 'Consumo de almacén'),
                    ('EFECTIVO_CAJA_COBRANZA', 'Efectivo en caja de cobranza (reglas de cobro 23-oct)'),
                    ('BANCOS_MN', 'Bancos moneda nacional (reglas de cobro 23-oct)'),
                    ('COBROS_TC_TD', 'Cobros con tarjeta de crédito o débito (reglas de cobro 23-oct)'),
                    ('COBROS_CHEQUE', 'Cobros con cheque (reglas de cobro 23-oct)'),
                    ('DEUDORES_FUNCIONARIOS_EMPLEADOS', 'Deudores diversos funcionarios y empleados (reglas de cobro 23-oct)'),
                    ('COMISIONES_BANCARIAS', 'Comisiones bancarias (reglas de cobro 23-oct)')
                ) AS r(codigo, descripcion)
                ON CONFLICT DO NOTHING;

                -- Plantilla #2 (plano C1 §4; ficha C1.4): factura de proveedor → TT. E6: líneas de servicio a GASTO por centro de costo, el
                -- resto al puente (mientras no llegue EsServicio, todo al puente). E7: el puente es MERCANCIA_RECIBIDA_POR_FACTURAR.
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, 'cuentas_por_pagar.factura.registrada.v1', 1, DATE '2026-01-01', 'TT',
                       'Factura de proveedor', 'Plantilla #2 del plano C1: factura de proveedor contra la cuenta puente, IVA pendiente y proveedor',
                       '[
                         {"Orden":1,"Rol":"MERCANCIA_RECIBIDA_POR_FACTURAR","Lado":"Cargo","Campo":"Subtotal","Auxiliar":"Ninguno","PorLinea":true,"SoloServicios":false,"ConCentroCosto":false,"Concepto":"Mercancía recibida por facturar"},
                         {"Orden":2,"Rol":"GASTO","Lado":"Cargo","Campo":"Subtotal","Auxiliar":"Ninguno","PorLinea":true,"SoloServicios":true,"ConCentroCosto":true,"Concepto":"Gasto por servicio"},
                         {"Orden":3,"Rol":"IVA_ACREDITABLE_PENDIENTE","Lado":"Cargo","Campo":"Iva","Auxiliar":"Ninguno","PorLinea":false,"SoloServicios":null,"ConCentroCosto":false,"Concepto":"IVA acreditable pendiente"},
                         {"Orden":4,"Rol":"PROVEEDORES","Lado":"Abono","Campo":"TotalMenosRetenciones","Auxiliar":"Proveedor","PorLinea":false,"SoloServicios":null,"ConCentroCosto":false,"Concepto":"Proveedor"},
                         {"Orden":5,"Rol":"RETENCIONES_POR_PAGAR","Lado":"Abono","Campo":"RetencionesTotal","Auxiliar":"Ninguno","PorLinea":false,"SoloServicios":null,"ConCentroCosto":false,"Concepto":"Retenciones por pagar"}
                       ]'::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C14', 'migration-C14', NULL
                FROM compartido.empresas e
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plantillas_contables",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "rechazos_contables",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "roles_cuenta_cambios",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "roles_cuenta",
                schema: "contabilidad");
        }
    }
}
