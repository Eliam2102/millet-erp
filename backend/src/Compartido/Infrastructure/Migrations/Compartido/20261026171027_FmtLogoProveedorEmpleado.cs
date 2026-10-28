using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class FmtLogoProveedorEmpleado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "calle",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ciudad",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_postal",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "colonia",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estado",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "municipio",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_exterior",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_interior",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pais",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_id",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "logo_actualizado_en",
                schema: "compartido",
                table: "empresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_actualizado_por",
                schema: "compartido",
                table: "empresas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_blob_ref",
                schema: "compartido",
                table: "empresas",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_content_type",
                schema: "compartido",
                table: "empresas",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extension",
                schema: "compartido",
                table: "empleados",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telefono",
                schema: "compartido",
                table: "empleados",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "calle",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "ciudad",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "codigo_postal",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "colonia",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "estado",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "municipio",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "numero_exterior",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "numero_interior",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "pais",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "tax_id",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "logo_actualizado_en",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "logo_actualizado_por",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "logo_blob_ref",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "logo_content_type",
                schema: "compartido",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "extension",
                schema: "compartido",
                table: "empleados");

            migrationBuilder.DropColumn(
                name: "telefono",
                schema: "compartido",
                table: "empleados");
        }
    }
}
