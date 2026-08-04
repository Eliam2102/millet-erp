using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndicesBandejasOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F10-PR2: índices adicionales para acelerar las bandejas
            // (F6-PR3) y reportes (F7-PR3) sobre la tabla ordenes_compra.
            // Los índices son aditivos; CREATE INDEX simple (lock breve,
            // aceptable para Millet en MVP — la tabla todavía no es grande).

            // Bandeja general: filtra por estado y ordena por fecha DESC.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_oc_estado_fecha
                ON compras.ordenes_compra (empresa_id, estado, fecha_documento DESC);
            ");

            // Bandeja pendientes-autorizacion: filtra por estado (2 valores) y ordena FIFO.
            // Valores estado: EnAutorizacionJefeCompras=1, EnAutorizacionDireccion=2.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_oc_pendientes_autorizacion
                ON compras.ordenes_compra (empresa_id, fecha_documento ASC)
                WHERE estado IN (1, 2);
            ");

            // Hermanas duplicadas: WHERE oc_origen_id = X.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_oc_origen
                ON compras.ordenes_compra (empresa_id, oc_origen_id)
                WHERE oc_origen_id IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS compras.ix_oc_estado_fecha;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS compras.ix_oc_pendientes_autorizacion;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS compras.ix_oc_origen;");
        }
    }
}
