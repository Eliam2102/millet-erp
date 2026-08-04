using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropSubAlmacenIdRecepcionOcLocal : Migration
    {
        // Almacén-por-línea 6b-3 (contract) — CIERRA el retiro de tres fases de
        // sub_almacen_id: relajar NOT NULL → nullable (6b-1) → dejar de mapear
        // (6b-2, builder.Ignore) → dropear (ESTA).
        //
        // ESCRITA A MANO a propósito. `dotnet ef migrations add` NO genera nada:
        // 6b-2b (AlinearSnapshotSinMapeoSubAlmacen) ya sacó la columna del snapshot,
        // así que no hay diff de modelo que EF pueda detectar. Por eso:
        //   - el Up() lleva el DropColumn escrito a mano;
        //   - el Designer.cs se copió TAL CUAL del de 6b-2b (mismo modelo objetivo),
        //     ajustando solo el [Migration("...")] y el nombre de la clase;
        //   - CuentasPorPagarDbContextModelSnapshot.cs NO se toca (ya estaba alineado).
        // Es una migración legítima con Up() real y CERO diff de snapshot. Que nadie
        // la "arregle" regenerando el snapshot ni la borre creyéndola código muerto.
        //
        // El DROP es WINDOW-FREE: desde 6b-2 (desplegado y vivo) ninguna instancia
        // mapea la columna, así que run-migrations puede dropearla mientras el backend
        // viejo corre, sin 42703.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sub_almacen_id",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down LOSSY (mismo criterio que 6b-1 y M1.Down de 6a): re-crea la
            // COLUMNA, pero NO los datos. El valor de sub_almacen_id venía del evento
            // almacen.oc_recepcion.registrada.v1 y desde 6b-1 ya nadie lo escribe;
            // desde 6b-2 ni siquiera se mapea. Revertir deja la columna nullable y
            // VACÍA (todo NULL) — restaurar los valores originales es imposible desde
            // aquí. Se documenta porque "hay Down" no significa "restaura el dato".
            migrationBuilder.AddColumn<Guid>(
                name: "sub_almacen_id",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local",
                type: "uuid",
                nullable: true);
        }
    }
}
