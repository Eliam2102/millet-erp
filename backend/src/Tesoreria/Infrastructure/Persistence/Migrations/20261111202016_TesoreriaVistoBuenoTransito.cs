using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaVistoBuenoTransito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_extracto_linea_hash",
                schema: "tesoreria",
                table: "extracto_linea");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conciliacion_estado",
                schema: "tesoreria",
                table: "conciliacion");

            migrationBuilder.AddColumn<string>(
                name: "partida_transito_causa",
                schema: "tesoreria",
                table: "extracto_linea",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "partida_transito_fecha_prevista",
                schema: "tesoreria",
                table: "extracto_linea",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "visto_bueno_en",
                schema: "tesoreria",
                table: "conciliacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "visto_bueno_por",
                schema: "tesoreria",
                table: "conciliacion",
                type: "uuid",
                nullable: true);

            // M-205: depura duplicados previos sin borrar líneas: el hash se queda solo en la primera
            // (menor número de línea) de cada (conciliación, hash).
            migrationBuilder.Sql(@"
                UPDATE tesoreria.extracto_linea l SET linea_hash = NULL
                FROM (SELECT id, ROW_NUMBER() OVER (PARTITION BY conciliacion_id, linea_hash ORDER BY numero_linea, id) AS rn
                      FROM tesoreria.extracto_linea WHERE linea_hash IS NOT NULL) d
                WHERE l.id = d.id AND d.rn > 1;");

            migrationBuilder.CreateIndex(
                name: "ux_extracto_linea_hash",
                schema: "tesoreria",
                table: "extracto_linea",
                columns: new[] { "conciliacion_id", "linea_hash" },
                unique: true,
                filter: "linea_hash IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conciliacion_estado",
                schema: "tesoreria",
                table: "conciliacion",
                sql: "estado IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_extracto_linea_hash",
                schema: "tesoreria",
                table: "extracto_linea");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conciliacion_estado",
                schema: "tesoreria",
                table: "conciliacion");

            migrationBuilder.DropColumn(
                name: "partida_transito_causa",
                schema: "tesoreria",
                table: "extracto_linea");

            migrationBuilder.DropColumn(
                name: "partida_transito_fecha_prevista",
                schema: "tesoreria",
                table: "extracto_linea");

            migrationBuilder.DropColumn(
                name: "visto_bueno_en",
                schema: "tesoreria",
                table: "conciliacion");

            migrationBuilder.DropColumn(
                name: "visto_bueno_por",
                schema: "tesoreria",
                table: "conciliacion");

            migrationBuilder.CreateIndex(
                name: "ix_extracto_linea_hash",
                schema: "tesoreria",
                table: "extracto_linea",
                columns: new[] { "conciliacion_id", "linea_hash" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_conciliacion_estado",
                schema: "tesoreria",
                table: "conciliacion",
                sql: "estado IN (1, 2)");
        }
    }
}
