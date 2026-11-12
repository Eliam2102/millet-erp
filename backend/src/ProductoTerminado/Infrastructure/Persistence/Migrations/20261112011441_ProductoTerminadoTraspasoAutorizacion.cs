using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoTraspasoAutorizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "autorizado_at",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "autorizado_por_id",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carta_porte_folio",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "carta_porte_id",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino_codigo_postal",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino_estado",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "distancia_km",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "folio",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "foranea",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "operador_id",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_codigo_postal",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_estado",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "peso_kg",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preparado_por_id",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "vehiculo_id",
                schema: "producto_terminado",
                table: "traspasos_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidencia_diferencia",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_diferencia",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolucion",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Los traspasos que ya existían salieron en el acto: su folio sale del día y del id, y su autorización es la de su salida.
            migrationBuilder.Sql("""
                UPDATE producto_terminado.traspasos_pt
                   SET folio = 'TR-' || to_char(salida_at AT TIME ZONE 'America/Merida', 'YYYYMMDD') || '-' || upper(right(replace(id::text, '-', ''), 5)),
                       autorizado_at = salida_at
                 WHERE folio = '';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_traspasos_pt_empresa_id_folio",
                schema: "producto_terminado",
                table: "traspasos_pt",
                columns: new[] { "empresa_id", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_traspasos_pt_empresa_id_folio",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "autorizado_at",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "autorizado_por_id",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "carta_porte_folio",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "carta_porte_id",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "destino",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "destino_codigo_postal",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "destino_estado",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "distancia_km",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "folio",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "foranea",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "operador_id",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "origen_codigo_postal",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "origen_estado",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "peso_kg",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "preparado_por_id",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "vehiculo_id",
                schema: "producto_terminado",
                table: "traspasos_pt");

            migrationBuilder.DropColumn(
                name: "clasificacion",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas");

            migrationBuilder.DropColumn(
                name: "evidencia_diferencia",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas");

            migrationBuilder.DropColumn(
                name: "motivo_diferencia",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas");

            migrationBuilder.DropColumn(
                name: "resolucion",
                schema: "producto_terminado",
                table: "traspaso_pt_piezas");
        }
    }
}
