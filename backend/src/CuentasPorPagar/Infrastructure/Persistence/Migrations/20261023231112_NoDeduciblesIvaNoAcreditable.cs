using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoDeduciblesIvaNoAcreditable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_gastos",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "clasificaciones_no_deducibles",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clasificaciones_no_deducibles", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "cuentas_por_pagar",
                table: "clasificaciones_no_deducibles",
                columns: new[] { "id", "activa", "clave", "created_at", "created_by", "deleted_at", "descripcion", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000007-000b-0000-0000-000000000001"), true, "COMIDAS", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", null, "Comidas", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", 1 },
                    { new Guid("00000007-000b-0000-0000-000000000002"), true, "VIATICOS_SIN_CFDI", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", null, "Viáticos sin CFDI", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", 1 },
                    { new Guid("00000007-000b-0000-0000-000000000003"), true, "PROPINAS", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", null, "Propinas", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", 1 },
                    { new Guid("00000007-000b-0000-0000-000000000004"), true, "ESTACIONAMIENTO", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", null, "Estacionamiento", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", 1 },
                    { new Guid("00000007-000b-0000-0000-000000000005"), true, "MULTAS_RECARGOS", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", null, "Multas y recargos", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", 1 },
                    { new Guid("00000007-000b-0000-0000-000000000006"), true, "OTROS", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", null, "Otros", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed-P8.6", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_clasificaciones_no_deducibles_clave",
                schema: "cuentas_por_pagar",
                table: "clasificaciones_no_deducibles",
                column: "clave",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clasificaciones_no_deducibles",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropColumn(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito");

            migrationBuilder.DropColumn(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos");

            migrationBuilder.DropColumn(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_gastos");

            migrationBuilder.DropColumn(
                name: "clasificacion_no_deducible",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
