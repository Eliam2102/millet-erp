using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeprecarOcBorradorStub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F5-PR3: deprecación de la tabla provisional usada por
            // InMemoryGenerarSolicitudCompraPort. La implementación real
            // (GenerarSolicitudCompraDesdeRqPort) ahora crea OCs reales en
            // compras.ordenes_compra. Vaciamos la tabla y la marcamos como
            // deprecated; el drop físico entra en una migración posterior
            // tras verificar dos releases que ninguna fila quedó.
            migrationBuilder.Sql("TRUNCATE TABLE compras.oc_borrador_stub;");
            migrationBuilder.Sql("COMMENT ON TABLE compras.oc_borrador_stub IS 'DEPRECATED F5-PR3: reemplazada por compras.ordenes_compra en flujo de bifurcación. Verificar que esté vacía antes del drop físico.';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("COMMENT ON TABLE compras.oc_borrador_stub IS NULL;");
        }
    }
}
