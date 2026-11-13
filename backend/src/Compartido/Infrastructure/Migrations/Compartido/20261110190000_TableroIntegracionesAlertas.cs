using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class TableroIntegracionesAlertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alertas_integracion",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuente_clave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fuente_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    abierta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notificada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recuperacion_notificada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alertas_integracion", x => x.id);
                    table.CheckConstraint("ck_alertas_integracion_cierre", "cerrada_en IS NULL OR cerrada_en >= abierta_en");
                    table.CheckConstraint("ck_alertas_integracion_tipo", "tipo IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_alertas_integracion_abierta_en",
                schema: "compartido",
                table: "alertas_integracion",
                column: "abierta_en");

            migrationBuilder.CreateIndex(
                name: "ux_alertas_integracion_abierta",
                schema: "compartido",
                table: "alertas_integracion",
                columns: new[] { "fuente_clave", "tipo" },
                unique: true,
                filter: "cerrada_en IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alertas_integracion",
                schema: "compartido");
        }
    }
}
