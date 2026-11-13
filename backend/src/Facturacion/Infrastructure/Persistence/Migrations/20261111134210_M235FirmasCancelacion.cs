using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M235FirmasCancelacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "firma_anulada_motivo",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "firmas_requeridas",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<decimal>(
                name: "importe_mxn",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "primera_firma_en",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primera_firma_huella",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primera_firma_por_id",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primera_firma_por_nombre",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Solicitudes vivas: importe en MXN del comprobante, para que su huella sea la de hoy.
            migrationBuilder.Sql("""
                UPDATE facturacion.solicitud_cancelacion s
                   SET importe_mxn = round(c.total * coalesce(c.tipo_cambio, 1), 2)
                  FROM facturacion.comprobante c
                 WHERE c.id = s.comprobante_id;
                """);

            migrationBuilder.CreateTable(
                name: "regla_firma_cancelacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_comprobante = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    umbral_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    firmas_requeridas = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regla_firma_cancelacion", x => x.id);
                    table.CheckConstraint("ck_regla_firma_cancelacion_firmas", "firmas_requeridas IN (1, 2)");
                    table.CheckConstraint("ck_regla_firma_cancelacion_umbral", "umbral_mxn >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_solicitud_cancelacion_firmas",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                sql: "firmas_requeridas IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_regla_firma_cancelacion",
                schema: "facturacion",
                table: "regla_firma_cancelacion",
                columns: new[] { "empresa_id", "tipo_comprobante", "umbral_mxn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "regla_firma_cancelacion",
                schema: "facturacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_solicitud_cancelacion_firmas",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "firma_anulada_motivo",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "firmas_requeridas",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "importe_mxn",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "primera_firma_en",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "primera_firma_huella",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "primera_firma_por_id",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "primera_firma_por_nombre",
                schema: "facturacion",
                table: "solicitud_cancelacion");
        }
    }
}
