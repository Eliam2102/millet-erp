using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CierreManualMotivoAplicaA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0043: el bitmask aplica_a admite ahora CierreManual = 16
            // (rango [1..31]). El CHECK debe ampliarse ANTES de insertar/actualizar
            // filas con el bit 16.
            migrationBuilder.DropCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo");

            migrationBuilder.AddCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo",
                sql: "aplica_a BETWEEN 1 AND 31");

            // El motivo libre "Otro" (RECH-OTRO, hoy aplica_a=15) también aplica
            // al cierre manual → 15 | 16 = 31. Da una opción de texto libre al
            // jefe de almacén sin tener que elegir un motivo específico.
            migrationBuilder.Sql(
                "UPDATE compras.motivos_rechazo SET aplica_a = 31 WHERE clave = 'RECH-OTRO';");

            // Motivo dedicado del cierre manual (aplica_a = 16, solo CierreManual).
            // GUID determinista en el namespace de motivos (00000003-0002-...).
            var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "compras",
                table: "motivos_rechazo",
                columns: new[] { "id", "clave", "descripcion", "permite_texto_libre", "aplica_a", "activo",
                                  "version", "created_at", "updated_at", "created_by", "updated_by", "deleted_at" },
                values: new object[,]
                {
                    { new Guid("00000003-0002-0000-0000-000000000007"), "CIERRE-NO-REQ", "Ya no se requiere el material", false, (short)16, true, 1, seedTime, seedTime, "seed", "seed", (DateTimeOffset?)null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo");

            // Revertir los datos con bit 16 ANTES de re-angostar el CHECK a [1..15],
            // si no la recreación del constraint fallaría con filas aplica_a>15.
            migrationBuilder.DeleteData(
                schema: "compras",
                table: "motivos_rechazo",
                keyColumn: "id",
                keyValue: new Guid("00000003-0002-0000-0000-000000000007"));

            migrationBuilder.Sql(
                "UPDATE compras.motivos_rechazo SET aplica_a = 15 WHERE clave = 'RECH-OTRO';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_motivos_rechazo_aplica_a",
                schema: "compras",
                table: "motivos_rechazo",
                sql: "aplica_a BETWEEN 1 AND 15");
        }
    }
}
