using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0047 C7.2c — el conteo físico baja a nivel rack. Agrega
    /// <c>ubicacion_id</c> (NOT NULL, FK a <c>ubicaciones</c>) a
    /// <c>lineas_conteo</c> y cambia el índice único de
    /// <c>(conteo, articulo, sub_almacen)</c> a
    /// <c>(conteo, articulo, ubicacion)</c> — que espeja la PK de saldos.
    ///
    /// <para><b>Robusta ante tabla no vacía (backfill a la ÚNICA):</b> la
    /// columna se agrega <c>NULL</c>, se rellena con la ubicación
    /// <c>es_default</c> (la ÚNICA) del <c>sub_almacen_id</c> de cada línea, y
    /// recién entonces se pone <c>NOT NULL</c>. Sirve igual en arranque nuevo
    /// (tabla vacía → el UPDATE no toca filas) que sobre una BD con conteos
    /// previos (Azure dev/prod). Si alguna línea quedara sin ÚNICA en su
    /// sub-almacén (estructura inconsistente), un guard lanza un error claro
    /// antes del <c>SET NOT NULL</c>. La precondición original de "tabla vacía"
    /// ya NO aplica (se hizo robusta tras el fallo del deploy de #498 en Azure
    /// dev, 2026-07-09, 23502: columna con valores null).</para>
    /// </summary>
    public partial class ConteoPorRack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo");

            // Robustez ante tabla no vacía: agregar NULL, backfill a la ÚNICA
            // del sub-almacén, y recién entonces NOT NULL. En arranque nuevo el
            // UPDATE no toca filas (tabla vacía).
            migrationBuilder.AddColumn<Guid>(
                name: "ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo",
                type: "uuid",
                nullable: true);

            // Backfill: cada línea vieja hereda la ubicación es_default (la
            // ÚNICA) de su sub-almacén. El índice parcial
            // ux_ubicaciones_default_por_sub_almacen garantiza ≤1 ÚNICA por
            // sub-almacén → el UPDATE es determinista.
            migrationBuilder.Sql(@"
                UPDATE almacen.lineas_conteo l
                SET ubicacion_id = u.id
                FROM almacen.ubicaciones u
                WHERE u.sub_almacen_id = l.sub_almacen_id AND u.es_default;");

            // Guard: si alguna línea quedó sin ÚNICA (estructura inconsistente),
            // fallar con un mensaje claro antes del SET NOT NULL (evita el 23502
            // críptico de Postgres).
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM almacen.lineas_conteo WHERE ubicacion_id IS NULL) THEN
                        RAISE EXCEPTION 'ConteoPorRack: hay lineas de conteo sin ubicacion es_default (UNICA) en su sub-almacen — estructura inconsistente; no se puede aplicar NOT NULL a ubicacion_id';
                    END IF;
                END $$;");

            migrationBuilder.AlterColumn<Guid>(
                name: "ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lineas_conteo_ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo",
                column: "ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo",
                columns: new[] { "conteo_id", "articulo_id", "ubicacion_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_lineas_conteo_ubicaciones_ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo",
                column: "ubicacion_id",
                principalSchema: "almacen",
                principalTable: "ubicaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lineas_conteo_ubicaciones_ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.DropIndex(
                name: "ix_lineas_conteo_ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.DropIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.DropColumn(
                name: "ubicacion_id",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.CreateIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo",
                columns: new[] { "conteo_id", "articulo_id", "sub_almacen_id" },
                unique: true);
        }
    }
}
