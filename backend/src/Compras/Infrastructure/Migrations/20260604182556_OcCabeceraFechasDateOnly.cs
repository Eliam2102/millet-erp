using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <summary>
    /// ADR-0040: la cabecera de OC pasa <c>fecha_documento</c> y
    /// <c>fecha_entrega_esperada</c> de <c>timestamptz</c>/<c>DateTimeOffset</c>
    /// a <c>date</c>/<c>DateOnly</c> (son fechas de calendario de negocio, no
    /// instantes).
    ///
    /// <para>
    /// El <c>USING</c> se hace explícito con <c>AT TIME ZONE 'America/Mexico_City'</c>
    /// para que el día preservado sea el día <b>visible en hora local de México</b>,
    /// no el truncado en la zona de sesión del servidor (que en Azure es UTC y
    /// correría un día las marcas vespertinas). EF generaría por defecto
    /// <c>AlterColumn</c> con <c>USING col::date</c> (zona de sesión); por eso se
    /// sustituye por SQL crudo.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class OcCabeceraFechasDateOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE compras.ordenes_compra " +
                "ALTER COLUMN fecha_documento TYPE date " +
                "USING (fecha_documento AT TIME ZONE 'America/Mexico_City')::date;");

            migrationBuilder.Sql(
                "ALTER TABLE compras.ordenes_compra " +
                "ALTER COLUMN fecha_entrega_esperada TYPE date " +
                "USING (fecha_entrega_esperada AT TIME ZONE 'America/Mexico_City')::date;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Inverso: date → timestamptz reconstruyendo la medianoche local
            // de México como instante (ej. 2026-06-07 → 2026-06-07T06:00:00Z).
            migrationBuilder.Sql(
                "ALTER TABLE compras.ordenes_compra " +
                "ALTER COLUMN fecha_documento TYPE timestamp with time zone " +
                "USING (fecha_documento::timestamp AT TIME ZONE 'America/Mexico_City');");

            migrationBuilder.Sql(
                "ALTER TABLE compras.ordenes_compra " +
                "ALTER COLUMN fecha_entrega_esperada TYPE timestamp with time zone " +
                "USING (fecha_entrega_esperada::timestamp AT TIME ZONE 'America/Mexico_City');");
        }
    }
}
