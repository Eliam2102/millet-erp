using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations;
[DbContext(typeof(TesoreriaDbContext))]
[Migration("20261011120000_IngresoSucursal")]
public partial class IngresoSucursal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(name: "sucursal_id", schema: "tesoreria", table: "movimiento_bancario", type: "uuid", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "sucursal_id", schema: "tesoreria", table: "movimiento_bancario");
}
