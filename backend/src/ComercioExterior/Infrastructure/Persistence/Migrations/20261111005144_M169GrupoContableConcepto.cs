using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-169 (Contabilidad, 10-nov): grupo contable de cada concepto de gasto de comercio exterior. Decide a qué rol de gasto carga la
    /// póliza del costo de importación. Valor inicial por código: honorarios → honorarios aduanales (1); fletes internacional y
    /// nacional y maniobras → fletes y maniobras (2); almacenaje → almacenajes (3); el resto → otros gastos de importación (4).
    /// Contabilidad lo cambia por concepto en Gastos y prorrateo › Configurar bases por concepto.
    /// </summary>
    public partial class M169GrupoContableConcepto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "grupo_contable",
                schema: "comercio_exterior",
                table: "conceptos_gasto",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.Sql("""
                UPDATE comercio_exterior.conceptos_gasto
                   SET grupo_contable = CASE codigo
                       WHEN 'HONORARIOS' THEN 1
                       WHEN 'FLETE_INT' THEN 2 WHEN 'FLETE_NAC' THEN 2 WHEN 'MANIOBRAS' THEN 2
                       WHEN 'ALMACENAJE' THEN 3
                       ELSE 4 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "grupo_contable",
                schema: "comercio_exterior",
                table: "conceptos_gasto");
        }
    }
}
