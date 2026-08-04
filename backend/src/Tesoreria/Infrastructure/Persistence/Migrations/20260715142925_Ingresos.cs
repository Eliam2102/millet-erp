using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Ingresos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deposito_confirmacion_propuesta",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.RenameColumn(
                name: "confirmada_por",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                newName: "resuelta_por");

            migrationBuilder.RenameColumn(
                name: "confirmada_en",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                newName: "resuelta_en");

            migrationBuilder.AlterColumn<Guid>(
                name: "cliente_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "deposito_ref",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moneda",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "monto_esperado",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_deposito_confirmacion_caja_sesion",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "caja_sesion_id",
                unique: true,
                filter: "caja_sesion_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_deposito_confirmacion_propuesta",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "propuesta_cxc_id",
                unique: true,
                filter: "propuesta_cxc_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "propuesta_cxc_id IS NOT NULL OR caja_sesion_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_deposito_confirmacion_caja_sesion",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropIndex(
                name: "ux_deposito_confirmacion_propuesta",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "deposito_ref",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "moneda",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "monto_esperado",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.RenameColumn(
                name: "resuelta_por",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                newName: "confirmada_por");

            migrationBuilder.RenameColumn(
                name: "resuelta_en",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                newName: "confirmada_en");

            migrationBuilder.AlterColumn<Guid>(
                name: "cliente_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deposito_confirmacion_propuesta",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "propuesta_cxc_id");
        }
    }
}
