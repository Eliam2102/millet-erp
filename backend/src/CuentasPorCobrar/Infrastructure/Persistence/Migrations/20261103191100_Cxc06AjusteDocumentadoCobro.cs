using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cxc06AjusteDocumentadoCobro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ajuste_autorizado_en",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ajuste_autorizado_por",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_ajuste",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recibo_pago_id",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "tipo_ajuste",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_propuesta_aplicacion_recibo_pago",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                column: "recibo_pago_id");

            // CXC-06: las propuestas con ajuste anteriores a esta versión ya llegaron a Tesorería sin documentarse. Se marcan como
            // diferencia menor autorizada (sin autorizador) para que su póliza no se detenga; quedan identificables por el motivo.
            migrationBuilder.Sql("""
                UPDATE cuentas_por_cobrar.propuesta_aplicacion_pago
                SET tipo_ajuste = 2,
                    motivo_ajuste = 'Ajuste registrado antes de CXC-06 (sin documentar)',
                    ajuste_autorizado_en = created_at
                WHERE ajuste_no_fiscal < 0 AND ajuste_autorizado_en IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_propuesta_aplicacion_recibo_pago",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "ajuste_autorizado_en",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "ajuste_autorizado_por",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "motivo_ajuste",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "recibo_pago_id",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "tipo_ajuste",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");
        }
    }
}
