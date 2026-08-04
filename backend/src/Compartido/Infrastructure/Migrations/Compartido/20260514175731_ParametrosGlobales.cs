using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ParametrosGlobales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parametros_globales",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valor = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    modulo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametros_globales", x => x.id);
                    table.CheckConstraint("ck_parametros_globales_tipo", "tipo BETWEEN 0 AND 3");
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "parametros_globales",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "tipo", "updated_at", "updated_by", "valor", "version" },
                values: new object[,]
                {
                    { new Guid("00000006-0001-0000-0000-000000000001"), "system.timezone-default", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Zona horaria por defecto del sistema (IANA TZ database).", null, (short)0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "America/Mexico_City", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000002"), "system.formato-fecha", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Formato de fecha por defecto para visualización en UI.", null, (short)0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "dd/MM/yyyy", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000003"), "system.redondeo-monetario", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Número de decimales para montos monetarios MXN.", null, (short)1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "2", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000004"), "system.idioma-default", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Idioma por defecto del sistema (BCP 47).", null, (short)0, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "es-MX", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_parametros_globales_clave",
                schema: "compartido",
                table: "parametros_globales",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parametros_globales_modulo",
                schema: "compartido",
                table: "parametros_globales",
                column: "modulo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parametros_globales",
                schema: "compartido");
        }
    }
}
