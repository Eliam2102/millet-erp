using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlignComprasSnapshotForNet10 : Migration
    {
        // Compras comparte audit_log con CoreDbContext, que es quien migra esa tabla.
        // EF Core 10 detecta el snapshot anterior como desactualizado aunque no
        // haya operaciones SQL pendientes para Compras. Se alinea el snapshot
        // sin alterar el esquema ni los datos existentes.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
