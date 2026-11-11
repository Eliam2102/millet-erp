using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class TcAvisoCorreoAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "compartido",
                table: "parametros_globales",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "tipo", "updated_at", "updated_by", "valor", "version" },
                values: new object[] { new Guid("00000006-0001-0000-0000-00000000000e"), "integraciones.aw.tipo-cambio.correos-aviso", new DateTimeOffset(new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Correos que reciben el aviso cuando el tipo de cambio hacia A+W se publica tarde o falla después de la hora límite; TI corre su tarea a mano ese día. Separe con coma; vacío apaga el correo.", "integraciones.aw", (short)0, new DateTimeOffset(new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "soportemillet@millet.com.mx, contadorgeneral@millet.com.mx, falejos@millet.com.mx", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-00000000000e"));
        }
    }
}
