using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MotivosRevisionConSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "motivos_revision",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    sla_dias = table.Column<int>(type: "integer", nullable: true),
                    dependencia_revisora_default_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                    table.PrimaryKey("pk_motivos_revision", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                columns: new[] { "id", "activo", "codigo", "created_at", "created_by", "deleted_at", "dependencia_revisora_default_codigo", "descripcion", "nombre", "sla_dias", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000007-1001-0000-0000-000000000001"), true, "DISCREPANCIA_OC", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "COMPRAS", "Monto factura supera tolerancia del proveedor", "Discrepancia con OC fuera de tolerancia", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000002"), true, "DANOS_MERCANCIA", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "ALMACEN", "Recepción reporta daño físico vía OcRecepcionRegistradaEvent", "Daños o defectos en mercancía", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000003"), true, "DIFERENCIA_PRECIO", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "COMPRAS", "Precio de factura diferente al acordado", "Diferencia de precio", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000004"), true, "DEVOLUCION_PENDIENTE", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "ALMACEN", "Devolución a proveedor abierta sin liquidar (Almacén sub-flujo 8.B)", "Devolución pendiente", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000005"), true, "NO_CONFORMIDAD", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "AREA_USUARIA", "Servicio no cumple especificación", "No conformidad con servicio o producto", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000006"), true, "TIEMPO_RESPUESTA", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "COMPRAS", "Entrega fuera de plazo", "Tiempo de respuesta del proveedor", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000007"), true, "CALIDAD_SERVICIO", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "AREA_USUARIA", "Servicio recibido no satisfactorio", "Calidad de servicio insuficiente", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000008"), true, "GARANTIA_ABIERTA", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "COMPRAS", "Reclamo no resuelto", "Reclamo de garantía abierto", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-000000000009"), true, "FALTA_REPP", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "CXP", "REPP no emitido pasados 5 días del pago", "Falta complemento de pago + 5 días", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-00000000000a"), true, "DOCUMENTACION_FISCAL", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "CXP", "CFDI con datos inválidos, RFC mal, fechas inconsistentes", "Documentación fiscal incompleta", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-00000000000b"), true, "DISPUTA_CONTRACTUAL", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "DIRECCION_F", "Reclamación legal", "Disputa contractual", 15, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-00000000000c"), true, "FIRMA_PENDIENTE", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "DIRECCION_F", "Autorización formal pendiente", "Pendiente firma o autorización", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-00000000000d"), true, "INDICACION_EXPRESA", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "CXP", "Bloqueo manual por instrucción (sin SLA — libera el solicitante)", "Indicación expresa", null, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000007-1001-0000-0000-00000000000e"), true, "OTROS", new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "CXP", "Caso no clasificable", "Otros motivos", 5, new DateTimeOffset(new DateTime(2026, 5, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ux_motivos_revision_codigo",
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                column: "codigo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "motivos_revision",
                schema: "cuentas_por_pagar");
        }
    }
}
