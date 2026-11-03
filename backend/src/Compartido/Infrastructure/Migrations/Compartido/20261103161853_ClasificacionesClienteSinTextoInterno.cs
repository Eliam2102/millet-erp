using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <summary>
    /// Quita el texto interno «Por validar con Millet (V37…)» de las clasificaciones de cliente
    /// sembradas. Solo toca las filas que siguen con el texto original, para no pisar ediciones.
    /// </summary>
    public partial class ClasificacionesClienteSinTextoInterno : Migration
    {
        private const string Interno = "Por validar con Millet (V37, Eliam 10-oct-2026).";
        private const string Limpio = "Clasificación comercial de clientes.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"UPDATE compartido.clasificaciones_cliente SET descripcion = '{Limpio}' WHERE descripcion = '{Interno}';");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"UPDATE compartido.clasificaciones_cliente SET descripcion = '{Interno}' WHERE descripcion = '{Limpio}';");
    }
}
