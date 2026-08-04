using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlinearSnapshotSinMapeoSubAlmacen : Migration
    {
        // Almacén-por-línea 6b-2b (HOTFIX de despliegue) — migración NO-OP a propósito.
        //
        // 6b-2 (#716) dejó de MAPEAR sub_almacen_id (builder.Ignore) SIN migración.
        // Eso creó un drift modelo↔snapshot que `dotnet ef database update` (el job
        // run-migrations del deploy) trata como ERROR en EF Core 9
        // (PendingModelChangesWarning → InvalidOperationException), bloqueando el
        // despliegue de TODO el backend. En este pipeline no existe un estado
        // "dejo de mapear sin cambio de esquema" que el gate acepte.
        //
        // Esta migración ALINEA el snapshot con el modelo (el snapshot regenerado ya
        // NO declara la columna) para que el gate pase, PERO su Up()/Down() están
        // VACÍOS a propósito: NO toca el esquema. La columna sub_almacen_id sigue
        // dormida en la BD.
        //
        // El DROP físico NO va aquí: dropear con el backend 6b-1 vivo (que aún mapea
        // la columna) daría 42703 en el INSERT/SELECT de recepciones → DLQ → recepción
        // perdida irrecuperable (sin acceso a Azure). El DROP va en 6b-3, cuando el
        // código desplegado sea el de 6b-2 y ninguna instancia viva mape la columna.
        //
        // OJO 6b-3: como este snapshot ya no declara la columna, `migrations add` no
        // generará nada. La migración de 6b-3 se escribe A MANO con un
        // migrationBuilder.DropColumn explícito (única migración del workstream a mano).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: ver comentario de la clase. Solo alinea el snapshot; no toca el esquema.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: el Up() no cambió nada, así que revertir tampoco hace nada.
        }
    }
}
