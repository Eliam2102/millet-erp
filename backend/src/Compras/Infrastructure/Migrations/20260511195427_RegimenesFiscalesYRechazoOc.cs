using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegimenesFiscalesYRechazoOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo");

            migrationBuilder.CreateTable(
                name: "regimenes_fiscales_articulo",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regimen_proveedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    regimen_articulo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    iva_porcentaje = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    retencion_isr_porcentaje = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regimenes_fiscales_articulo", x => x.id);
                    table.CheckConstraint("ck_regfa_isr_rango", "retencion_isr_porcentaje IS NULL OR retencion_isr_porcentaje BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_regfa_iva_rango", "iva_porcentaje BETWEEN 0 AND 1");
                });

            migrationBuilder.InsertData(
                schema: "compras",
                table: "regimenes_fiscales_articulo",
                columns: new[] { "id", "activo", "created_at", "created_by", "deleted_at", "iva_porcentaje", "regimen_articulo", "regimen_proveedor", "retencion_isr_porcentaje", "updated_at", "updated_by", "version", "vigente_desde", "vigente_hasta" },
                values: new object[,]
                {
                    { new Guid("00000003-0003-0020-0000-000000000001"), true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 0.16m, "GENERAL", "GENERAL", null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000003-0003-0020-0000-000000000002"), true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 0m, "EXENTO", "GENERAL", null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000003-0003-0020-0000-000000000003"), true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 0m, "TASA_CERO", "GENERAL", null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("00000003-0003-0020-0000-000000000004"), true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 0.16m, "SERVICIO_PROFESIONAL", "GENERAL", 0.10m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo",
                sql: "aplica_a BETWEEN 1 AND 15");

            // F3-PR2: extender el bitmask aplica_a de los 6 motivos del seed
            // existente para incluir OrdenCompra=8. Los 6 motivos genéricos
            // (RECH-DUP, RECH-INSUF, RECH-INCOR, RECH-PROV, RECH-PRESUP,
            // RECH-OTRO) aplican también al rechazo de OC. aplica_a pasa de
            // 7 (Rechazo|Eliminacion|Cancelacion) a 15 (+ OrdenCompra).
            migrationBuilder.Sql(
                "UPDATE compras.motivos_rechazo SET aplica_a = 15 WHERE aplica_a = 7;");

            migrationBuilder.CreateIndex(
                name: "uq_regfa_par",
                schema: "compras",
                table: "regimenes_fiscales_articulo",
                columns: new[] { "regimen_proveedor", "regimen_articulo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "regimenes_fiscales_articulo",
                schema: "compras");

            migrationBuilder.DropCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo");

            // Revertir el UPDATE del bitmask (15 → 7) antes de re-establecer
            // el CHECK original con rango más restrictivo.
            migrationBuilder.Sql(
                "UPDATE compras.motivos_rechazo SET aplica_a = 7 WHERE aplica_a = 15;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo",
                sql: "aplica_a BETWEEN 1 AND 7");
        }
    }
}
