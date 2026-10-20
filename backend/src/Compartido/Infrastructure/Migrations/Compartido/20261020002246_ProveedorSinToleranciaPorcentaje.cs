using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorSinToleranciaPorcentaje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Decisión del owner: un monto en % no se puede reinterpretar como MXN; vuelve a la tolerancia general.
            migrationBuilder.Sql(
                "UPDATE compartido.proveedores SET tolerancia_factura_contra_oc_mxn = NULL WHERE tolerancia_factura_contra_oc_es_porcentaje;");

            migrationBuilder.DropColumn(
                name: "tolerancia_factura_contra_oc_es_porcentaje",
                schema: "compartido",
                table: "proveedores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "tolerancia_factura_contra_oc_es_porcentaje",
                schema: "compartido",
                table: "proveedores",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
