using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DevolucionClienteFolioYConcepto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "folio",
                schema: "tesoreria",
                table: "devolucion_cliente",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "folio_secuencias_devolucion_cliente",
                schema: "tesoreria",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    siguiente = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_secuencias_devolucion_cliente", x => x.empresa_id);
                });

            migrationBuilder.InsertData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                columns: new[] { "id", "activo", "clasificacion_flujo", "created_at", "created_by", "deleted_at", "es_ejemplo", "nombre", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("0000000b-1001-0000-0000-00000000000e"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, true, "Devolución a cliente", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            // Backfill: folio en orden de creación por empresa y secuencia al siguiente número libre.
            migrationBuilder.Sql(@"
                UPDATE tesoreria.devolucion_cliente d
                SET folio = 'DEV-T-' || lpad(n.rn::text, 4, '0')
                FROM (SELECT id, row_number() OVER (PARTITION BY empresa_id ORDER BY solicitada_en, id) AS rn
                      FROM tesoreria.devolucion_cliente) n
                WHERE d.id = n.id;

                INSERT INTO tesoreria.folio_secuencias_devolucion_cliente (empresa_id, siguiente)
                SELECT empresa_id, count(*) + 1 FROM tesoreria.devolucion_cliente GROUP BY empresa_id;

                -- C-04: los egresos de devoluciones ya pagadas dejan de usar un concepto de ingreso.
                UPDATE tesoreria.movimiento_bancario
                SET concepto_id = '0000000b-1001-0000-0000-00000000000e'
                WHERE id IN (SELECT movimiento_pago_id FROM tesoreria.devolucion_cliente WHERE movimiento_pago_id IS NOT NULL);
            ");

            migrationBuilder.CreateIndex(
                name: "ux_devolucion_cliente_empresa_folio",
                schema: "tesoreria",
                table: "devolucion_cliente",
                columns: new[] { "empresa_id", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "folio_secuencias_devolucion_cliente",
                schema: "tesoreria");

            migrationBuilder.DropIndex(
                name: "ux_devolucion_cliente_empresa_folio",
                schema: "tesoreria",
                table: "devolucion_cliente");

            migrationBuilder.DeleteData(
                schema: "tesoreria",
                table: "concepto_movimiento",
                keyColumn: "id",
                keyValue: new Guid("0000000b-1001-0000-0000-00000000000e"));

            migrationBuilder.DropColumn(
                name: "folio",
                schema: "tesoreria",
                table: "devolucion_cliente");
        }
    }
}
