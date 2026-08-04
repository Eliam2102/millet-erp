using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <summary>
    /// F2-PR3 — wiring de VOs <c>ContactoProveedor</c>,
    /// <c>InformacionLogistica</c>, <c>InformacionImportacion</c> y
    /// <c>ReferenciaProveedor</c> al agregado <c>OrdenCompra</c>.
    ///
    /// <para>
    /// Migración **intencionalmente no-op**: las columnas inline en
    /// <c>compras.ordenes_compra</c> (referencia_proveedor,
    /// contacto_proveedor_*, info_logistica_*, info_import_*) ya existen
    /// desde F1-PR1 como shadow properties. F2-PR3 las promueve a
    /// propiedades reales del agregado (mismos nombres snake_case) y
    /// agrega getters computed para los VOs. El modelo cambia, el schema
    /// no.
    /// </para>
    ///
    /// <para>
    /// La migración existe para que <c>__EFMigrationsHistory</c> registre
    /// el cambio del snapshot — sin ella, futuras migraciones generarían
    /// deltas incorrectos contra un snapshot desactualizado.
    /// </para>
    /// </summary>
    public partial class LogisticaImportacionWiring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op intencional. Ver doc en el resumen de clase.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op intencional.
        }
    }
}
