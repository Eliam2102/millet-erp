using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ActivosFijosDbContext))]
[Migration("20261109235900_M078TasaContableComputo")]
public sealed class M078TasaContableComputo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE activos_fijos.clases
            SET tasa_contable_anual = 30
            WHERE id = '0000000f-0007-0000-0000-000000000156'
              AND empresa_id = '00000003-0000-0000-0000-000000000001'
              AND clave = '156'
              AND tasa_contable_anual = 33;
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Revertir el binario no revierte una tasa contable confirmada por Millet.
    }
}
