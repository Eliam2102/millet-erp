using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CuentasPorCobrarPermisosCanonicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("0000000a-0001-0000-0000-000000000001"), "leer", "cuentas_por_cobrar.lineas-credito.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar líneas de crédito de clientes", "cuentas_por_cobrar", "lineas-credito", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0001-0000-0000-000000000002"), "gestionar", "cuentas_por_cobrar.lineas-credito.gestionar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Crear, editar, bloquear y desbloquear líneas de crédito", "cuentas_por_cobrar", "lineas-credito", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0002-0000-0000-000000000001"), "decidir", "cuentas_por_cobrar.liberacion.decidir", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Decidir la liberación de pedidos por crédito", "cuentas_por_cobrar", "liberacion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0002-0000-0000-000000000002"), "override", "cuentas_por_cobrar.liberacion.override", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Otorgar autorización consumible de override de crédito (gerente)", "cuentas_por_cobrar", "liberacion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0003-0000-0000-000000000001"), "registrar", "cuentas_por_cobrar.cobranza.registrar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Registrar gestiones de cobranza (llamadas, correos, promesas de pago)", "cuentas_por_cobrar", "cobranza", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0004-0000-0000-000000000001"), "leer", "cuentas_por_cobrar.cartera.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar cartera, antigüedad de saldos, estado de cuenta y alertas", "cuentas_por_cobrar", "cartera", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0005-0000-0000-000000000001"), "proponer", "cuentas_por_cobrar.aplicacion-pago.proponer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Crear propuestas de aplicación de pago desde depósitos", "cuentas_por_cobrar", "aplicacion-pago", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-0005-0000-0000-000000000002"), "confirmar", "cuentas_por_cobrar.aplicacion-pago.confirmar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Confirmar o rechazar propuestas de aplicación de pago (Ingresos)", "cuentas_por_cobrar", "aplicacion-pago", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0001-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0001-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0002-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0002-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0003-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0004-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0005-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000a-0005-0000-0000-000000000002"));
        }
    }
}
