using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelajarSubAlmacenIdNullableRecepcionOcLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "sub_almacen_id",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down LOSSY (mismo criterio que M1.Down de 6a): re-endurece a
            // NOT NULL, pero revertir 6b-1 NO restaura los valores — los
            // INVENTA. El defaultValue Guid.Empty estampa un GUID cero sobre
            // cualquier fila con sub_almacen_id NULL, que es justo lo que 6b-1
            // empieza a producir (el código nuevo escribe null). Con 0 filas es
            // inocuo; con filas nullable, esas quedan con 00000000-... en
            // silencio. Es lo que EF genera siempre para re-imponer NOT NULL;
            // se documenta porque "hay Down" no significa "restaura el dato".
            migrationBuilder.AlterColumn<Guid>(
                name: "sub_almacen_id",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
