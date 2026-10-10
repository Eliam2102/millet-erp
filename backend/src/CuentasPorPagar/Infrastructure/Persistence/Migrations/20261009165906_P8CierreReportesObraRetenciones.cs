using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8CierreReportesObraRetenciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alerta_retenciones",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "concepto_retencion",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "obra",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "movimientos_pasivo",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_pasivo", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimientos_pasivo_facturas_proveedor_factura_proveedor_id",
                        column: x => x.factura_proveedor_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "facturas_proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retenciones_concepto",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    impuesto = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tasa = table.Column<decimal>(type: "numeric(12,8)", precision: 12, scale: 8, nullable: false),
                    fuente = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_cambio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retenciones_concepto", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "cuentas_por_pagar",
                table: "retenciones_concepto",
                columns: new[] { "id", "activa", "concepto", "created_at", "created_by", "deleted_at", "descripcion", "fuente", "impuesto", "motivo_cambio", "tasa", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000007-000a-0000-0000-000000000001"), true, "HONORARIOS_PF", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", null, "Honorarios de persona física a persona moral", "https://wwwmat.sat.gob.mx/ordenamiento/18355/ley-del-impuesto-sobre-la-renta", "001", "Supuesto SAT, valida Fiscal (D03)", 0.10m, new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000002"), true, "HONORARIOS_PF", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", null, "IVA: dos terceras partes a tasa general de 16 %", "https://www.sat.gob.mx/minisitio/Factura/documentos/honorarios_servicios_contables.pdf", "002", "Supuesto SAT, valida Fiscal (D03)", 0.10666667m, new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000003"), true, "ARRENDAMIENTO_PF", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", null, "Arrendamiento de persona física a persona moral", "https://wwwmat.sat.gob.mx/ordenamiento/18355/ley-del-impuesto-sobre-la-renta", "001", "Supuesto SAT, valida Fiscal (D03)", 0.10m, new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000004"), true, "ARRENDAMIENTO_PF", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", null, "IVA: dos terceras partes a tasa general de 16 %", "https://www.sat.gob.mx/minisitio/Factura/documentos/arrendamiento_local_comercial.pdf", "002", "Supuesto SAT, valida Fiscal (D03)", 0.10666667m, new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000005"), true, "FLETES", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", null, "Autotransporte terrestre de bienes recibido por persona moral", "https://www.sat.gob.mx/cs/Satellite?blobcol=urldata&blobkey=id&blobtable=MungoBlobs&blobwhere=1461175803212&ssbinary=true", "002", "Supuesto SAT, valida Fiscal (D03)", 0.04m, new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", 1 },
                    { new Guid("00000007-000a-0000-0000-000000000006"), true, "RESICO_PF", new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", null, "Pagos de persona moral a persona física RESICO; revisar excepciones", "https://wwwmat.sat.gob.mx/articulo/59511/articulo-113-j", "001", "Supuesto SAT, valida Fiscal (D03)", 0.0125m, new DateTimeOffset(new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_pasivo_factura_proveedor_id_fecha",
                schema: "cuentas_por_pagar",
                table: "movimientos_pasivo",
                columns: new[] { "factura_proveedor_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_retenciones_concepto_concepto_impuesto_tasa",
                schema: "cuentas_por_pagar",
                table: "retenciones_concepto",
                columns: new[] { "concepto", "impuesto", "tasa" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimientos_pasivo",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "retenciones_concepto",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropColumn(
                name: "alerta_retenciones",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "concepto_retencion",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "obra",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
