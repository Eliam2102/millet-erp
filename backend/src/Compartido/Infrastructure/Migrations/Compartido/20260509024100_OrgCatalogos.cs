using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class OrgCatalogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "departamentos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
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
                    table.PrimaryKey("pk_departamentos", x => x.id);
                    table.CheckConstraint("ck_departamentos_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "sucursales",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
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
                    table.PrimaryKey("pk_sucursales", x => x.id);
                    table.CheckConstraint("ck_sucursales_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "almacenes",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_almacenes", x => x.id);
                    table.CheckConstraint("ck_almacenes_estatus", "estatus BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_almacenes_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalSchema: "compartido",
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_clave",
                schema: "compartido",
                table: "almacenes",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_estatus",
                schema: "compartido",
                table: "almacenes",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_sucursal_id",
                schema: "compartido",
                table: "almacenes",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_clave",
                schema: "compartido",
                table: "departamentos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_estatus",
                schema: "compartido",
                table: "departamentos",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_clave",
                schema: "compartido",
                table: "sucursales",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_estatus",
                schema: "compartido",
                table: "sucursales",
                column: "estatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "almacenes",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "departamentos",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "sucursales",
                schema: "compartido");
        }
    }
}
