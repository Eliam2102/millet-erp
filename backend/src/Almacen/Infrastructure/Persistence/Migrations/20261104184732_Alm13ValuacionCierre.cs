using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Alm13ValuacionCierre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "valuaciones_cierre",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_cerrado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_aw_variante_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lote = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    valor_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valuaciones_cierre", x => x.id);
                    table.ForeignKey(
                        name: "fk_valuaciones_cierre_periodos_cerrados_periodo_cerrado_id",
                        column: x => x.periodo_cerrado_id,
                        principalSchema: "almacen",
                        principalTable: "periodos_cerrados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_valuaciones_cierre_empresa_id_anio_mes_sub_almacen_id",
                schema: "almacen",
                table: "valuaciones_cierre",
                columns: new[] { "empresa_id", "anio", "mes", "sub_almacen_id" });

            migrationBuilder.CreateIndex(
                name: "ix_valuaciones_cierre_periodo_cerrado_id",
                schema: "almacen",
                table: "valuaciones_cierre",
                column: "periodo_cerrado_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "valuaciones_cierre",
                schema: "almacen");
        }
    }
}
