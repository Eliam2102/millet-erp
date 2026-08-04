using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSucursalToEntidadExterna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sucursal — código corto de 3 chars (CIR/CHI/CAN/CON) que define
            // qué customizing de A+W procesa el EDI vía pattern matching del
            // filename `cot_<SUCURSAL>_<QUOTE_REF>.edi`.
            //
            // Estrategia para hard cutover con filas legacy:
            //
            //   1. AddColumn NOT NULL DEFAULT 'CIR'  ← backfill arbitrario para
            //      filas históricas (cotizaciones de prueba en dev). Si en algún
            //      ambiente las filas legacy necesitan otra sucursal, ajustar
            //      antes del deploy o correr UPDATE post-deploy.
            //
            //   2. DROP DEFAULT inmediatamente  ← cada INSERT futuro debe
            //      proveer el valor explícitamente. Sin esto, un caller que
            //      olvide setearlo recibe 'CIR' silenciosamente (peor que un
            //      error claro).
            //
            // El validador FluentValidation del command rechaza sucursales fuera
            // del catálogo, así que dev/prod nunca persisten valores inválidos
            // post-rollout.
            migrationBuilder.AddColumn<string>(
                name: "sucursal",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "CIR");

            migrationBuilder.Sql(@"
                ALTER TABLE integraciones_aw.entidad_externa
                ALTER COLUMN sucursal DROP DEFAULT;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sucursal",
                schema: "integraciones_aw",
                table: "entidad_externa");
        }
    }
}
