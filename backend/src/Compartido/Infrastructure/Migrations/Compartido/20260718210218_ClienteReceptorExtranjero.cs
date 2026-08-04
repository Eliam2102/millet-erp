using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ClienteReceptorExtranjero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "domicilio_extranjero_calle",
                schema: "compartido",
                table: "clientes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "domicilio_extranjero_codigo_postal",
                schema: "compartido",
                table: "clientes",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "domicilio_extranjero_estado",
                schema: "compartido",
                table: "clientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "num_reg_id_trib",
                schema: "compartido",
                table: "clientes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pais_residencia",
                schema: "compartido",
                table: "clientes",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_clientes_pais_residencia",
                schema: "compartido",
                table: "clientes",
                sql: "pais_residencia IS NULL OR pais_residencia ~ '^[A-Za-z]{3}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_clientes_pais_residencia",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "domicilio_extranjero_calle",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "domicilio_extranjero_codigo_postal",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "domicilio_extranjero_estado",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "num_reg_id_trib",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "pais_residencia",
                schema: "compartido",
                table: "clientes");
        }
    }
}
