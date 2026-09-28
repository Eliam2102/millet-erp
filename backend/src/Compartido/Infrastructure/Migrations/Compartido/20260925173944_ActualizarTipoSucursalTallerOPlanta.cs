using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ActualizarTipoSucursalTallerOPlanta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sucursales_tipo",
                schema: "compartido",
                table: "sucursales");

            // Migrar sucursales legadas con tipo = 0 (Matriz): MID a Planta (2), resto a Taller (1)
            migrationBuilder.Sql("UPDATE compartido.sucursales SET tipo = 2 WHERE tipo = 0 AND clave = 'MID';");
            migrationBuilder.Sql("UPDATE compartido.sucursales SET tipo = 1 WHERE tipo = 0;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sucursales_tipo",
                schema: "compartido",
                table: "sucursales",
                sql: "tipo BETWEEN 1 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sucursales_tipo",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sucursales_tipo",
                schema: "compartido",
                table: "sucursales",
                sql: "tipo BETWEEN 0 AND 2");
        }
    }
}
