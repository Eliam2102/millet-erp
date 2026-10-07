using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadRubrosYTipoDerivado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "clase",
                schema: "contabilidad",
                table: "cuentas_contables",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "rubro_id",
                schema: "contabilidad",
                table: "cuentas_contables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_contables_rubro_id",
                schema: "contabilidad",
                table: "cuentas_contables",
                column: "rubro_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cuentas_rubro",
                schema: "contabilidad",
                table: "cuentas_contables",
                sql: "(clase = 0 AND (rubro_id IS NULL OR padre_id IS NULL)) OR (clase = 1 AND padre_id IS NULL AND rubro_id IS NULL AND cuenta_control = 0)");

            migrationBuilder.AddForeignKey(
                name: "fk_cuentas_contables_cuentas_contables_rubro_id",
                schema: "contabilidad",
                table: "cuentas_contables",
                column: "rubro_id",
                principalSchema: "contabilidad",
                principalTable: "cuentas_contables",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // P19: el tipo ya no queda pendiente; las cuentas cargadas antes sin tipo lo reciben por la jerarquía
            // (nivel 1 o con hijas ⇒ acumula; resto ⇒ afectable). Solo toca filas con tipo NULL; el Down no lo revierte.
            migrationBuilder.Sql("""
                UPDATE contabilidad.cuentas_contables c
                SET tipo = CASE WHEN c.nivel = 1
                    OR EXISTS (SELECT 1 FROM contabilidad.cuentas_contables h WHERE h.padre_id = c.id) THEN 0 ELSE 1 END
                WHERE c.tipo IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cuentas_contables_cuentas_contables_rubro_id",
                schema: "contabilidad",
                table: "cuentas_contables");

            migrationBuilder.DropIndex(
                name: "ix_cuentas_contables_rubro_id",
                schema: "contabilidad",
                table: "cuentas_contables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cuentas_rubro",
                schema: "contabilidad",
                table: "cuentas_contables");

            migrationBuilder.DropColumn(
                name: "clase",
                schema: "contabilidad",
                table: "cuentas_contables");

            migrationBuilder.DropColumn(
                name: "rubro_id",
                schema: "contabilidad",
                table: "cuentas_contables");
        }
    }
}
