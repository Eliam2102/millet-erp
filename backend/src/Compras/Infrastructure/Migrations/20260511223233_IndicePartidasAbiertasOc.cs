using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndicePartidasAbiertasOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F7-PR1: índice parcial sobre OCs no-terminales con alguna
            // dimensión abierta. Acelera el query de partidas-abiertas
            // (5k OCs activas vs 50k históricas → seq scan inviable).
            // Cobertura: (empresa_id, fecha_entrega_esperada) ordenado.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_oc_partidas_abiertas
                ON compras.ordenes_compra (empresa_id, fecha_entrega_esperada, fecha_documento)
                WHERE estado NOT IN (4, 5, 6)
                  AND (sub_estado_recepcion <> 2
                       OR sub_estado_facturacion <> 2
                       OR sub_estado_pago <> 2);
            ");
            // Valores de los enums:
            //   EstadoOrdenCompra: Borrador=0, EnAutorizacionJefeCompras=1,
            //     EnAutorizacionDireccion=2, Autorizada=3, Cerrada=4,
            //     Cancelada=5, Rechazada=6.
            //   SubEstadoRecepcion: SinRecepcion=0, Parcial=1, Completa=2.
            //   SubEstadoFacturacion: SinFactura=0, Parcial=1, Completa=2.
            //   SubEstadoPago: SinPago=0, Parcial=1, Pagada=2.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS compras.ix_oc_partidas_abiertas;");
        }
    }
}
