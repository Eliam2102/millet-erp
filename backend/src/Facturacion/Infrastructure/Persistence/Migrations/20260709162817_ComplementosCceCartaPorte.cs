using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplementosCceCartaPorte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "peso_bruto_vehicular",
                schema: "facturacion",
                table: "vehiculo",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "certificado_origen",
                schema: "facturacion",
                table: "complemento_cce",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "clave_de_pedimento",
                schema: "facturacion",
                table: "complemento_cce",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receptor_domicilio_calle",
                schema: "facturacion",
                table: "complemento_cce",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receptor_domicilio_codigo_postal",
                schema: "facturacion",
                table: "complemento_cce",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receptor_domicilio_estado",
                schema: "facturacion",
                table: "complemento_cce",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino_codigo_postal",
                schema: "facturacion",
                table: "carta_porte",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino_estado",
                schema: "facturacion",
                table: "carta_porte",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_codigo_postal",
                schema: "facturacion",
                table: "carta_porte",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen_estado",
                schema: "facturacion",
                table: "carta_porte",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "peso_bruto_vehicular",
                schema: "facturacion",
                table: "vehiculo");

            migrationBuilder.DropColumn(
                name: "certificado_origen",
                schema: "facturacion",
                table: "complemento_cce");

            migrationBuilder.DropColumn(
                name: "clave_de_pedimento",
                schema: "facturacion",
                table: "complemento_cce");

            migrationBuilder.DropColumn(
                name: "receptor_domicilio_calle",
                schema: "facturacion",
                table: "complemento_cce");

            migrationBuilder.DropColumn(
                name: "receptor_domicilio_codigo_postal",
                schema: "facturacion",
                table: "complemento_cce");

            migrationBuilder.DropColumn(
                name: "receptor_domicilio_estado",
                schema: "facturacion",
                table: "complemento_cce");

            migrationBuilder.DropColumn(
                name: "destino_codigo_postal",
                schema: "facturacion",
                table: "carta_porte");

            migrationBuilder.DropColumn(
                name: "destino_estado",
                schema: "facturacion",
                table: "carta_porte");

            migrationBuilder.DropColumn(
                name: "origen_codigo_postal",
                schema: "facturacion",
                table: "carta_porte");

            migrationBuilder.DropColumn(
                name: "origen_estado",
                schema: "facturacion",
                table: "carta_porte");
        }
    }
}
