using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class UnidadMedidaCatalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "unidades_medida",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dimension = table.Column<short>(type: "smallint", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    decimales = table.Column<int>(type: "integer", nullable: false),
                    es_base = table.Column<bool>(type: "boolean", nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidades_medida", x => x.id);
                    table.CheckConstraint("ck_unidades_medida_decimales", "decimales BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_unidades_medida_dimension", "dimension BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_unidades_medida_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_unidades_medida_factor_positivo", "factor_a_base > 0");
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "unidades_medida",
                columns: new[] { "id", "codigo", "created_at", "created_by", "decimales", "deleted_at", "dimension", "es_base", "estatus", "factor_a_base", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0007-0000-0000-000000000001"), "PZA", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 0, null, (short)0, true, (short)0, 1m, "Pieza", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000002"), "PAR", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 0, null, (short)0, false, (short)0, 2m, "Par", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000003"), "KG", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 3, null, (short)1, true, (short)0, 1m, "Kilogramo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000004"), "G", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 2, null, (short)1, false, (short)0, 0.001m, "Gramo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000005"), "L", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 3, null, (short)2, true, (short)0, 1m, "Litro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000006"), "ML", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 0, null, (short)2, false, (short)0, 0.001m, "Mililitro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000007"), "M", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 2, null, (short)3, true, (short)0, 1m, "Metro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000008"), "CM", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1, null, (short)3, false, (short)0, 0.01m, "Centímetro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-000000000009"), "MM", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 0, null, (short)3, false, (short)0, 0.001m, "Milímetro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0007-0000-0000-00000000000a"), "HR", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 2, null, (short)4, true, (short)0, 1m, "Hora", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_unidades_medida_codigo",
                schema: "compartido",
                table: "unidades_medida",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unidades_medida_dimension",
                schema: "compartido",
                table: "unidades_medida",
                column: "dimension");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unidades_medida",
                schema: "compartido");
        }
    }
}
