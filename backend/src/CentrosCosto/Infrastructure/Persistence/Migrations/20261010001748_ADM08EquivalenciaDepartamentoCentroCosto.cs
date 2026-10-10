using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CentrosCosto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ADM08EquivalenciaDepartamentoCentroCosto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "departamento_centros_costo",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    centro_costo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departamento_centros_costo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_departamento_centros_costo_empresa_id_sucursal_id_departame",
                schema: "centros_costo",
                table: "departamento_centros_costo",
                columns: new[] { "empresa_id", "sucursal_id", "departamento_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "departamento_centros_costo",
                schema: "centros_costo");
        }
    }
}
