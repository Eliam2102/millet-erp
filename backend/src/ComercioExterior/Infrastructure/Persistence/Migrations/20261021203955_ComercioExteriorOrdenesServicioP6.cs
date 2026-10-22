using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorOrdenesServicioP6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "orden_servicio_asignado",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "orden_servicio_folio",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "orden_servicio_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "orden_servicio_moneda",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "orden_servicio_tipo_cambio",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "numeric(20,6)",
                precision: 20,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "orden_servicio_total",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requiere_orden_servicio",
                schema: "comercio_exterior",
                table: "conceptos_gasto",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE comercio_exterior.conceptos_gasto
                SET requiere_orden_servicio = TRUE
                WHERE codigo IN ('ADUANALES', 'HONORARIOS');
                INSERT INTO comercio_exterior.conceptos_gasto
                    (id, empresa_id, codigo, nombre, base_importacion, base_exportacion,
                     requiere_orden_servicio, version, created_at, updated_at)
                SELECT gen_random_uuid(), empresas.empresa_id, 'ADUANALES', 'Gastos aduanales', 1, 2,
                       TRUE, 1, now(), now()
                FROM (SELECT DISTINCT empresa_id FROM comercio_exterior.conceptos_gasto) empresas
                WHERE NOT EXISTS (SELECT 1 FROM comercio_exterior.conceptos_gasto c
                                  WHERE c.empresa_id = empresas.empresa_id AND c.codigo = 'ADUANALES');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_gastos_expediente_empresa_id_orden_servicio_id_expediente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                columns: new[] { "empresa_id", "orden_servicio_id", "expediente_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gastos_expediente_empresa_id_orden_servicio_id_expediente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "orden_servicio_asignado",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "orden_servicio_folio",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "orden_servicio_id",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "orden_servicio_moneda",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "orden_servicio_tipo_cambio",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "orden_servicio_total",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "requiere_orden_servicio",
                schema: "comercio_exterior",
                table: "conceptos_gasto");
        }
    }
}
