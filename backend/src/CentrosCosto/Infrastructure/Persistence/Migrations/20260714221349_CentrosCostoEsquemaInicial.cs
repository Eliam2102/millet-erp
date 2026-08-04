using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CentrosCosto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CentrosCostoEsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "centros_costo");

            migrationBuilder.CreateTable(
                name: "grupos",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("pk_grupos", x => x.id);
                    table.CheckConstraint("ck_grupos_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "subgrupos",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("pk_subgrupos", x => x.id);
                    table.CheckConstraint("ck_subgrupos_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "sucursales_ceco",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_ceco = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
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
                    table.PrimaryKey("pk_sucursales_ceco", x => x.id);
                    table.CheckConstraint("ck_sucursales_ceco_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "departamentos",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_centro_costo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    grupo_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.ForeignKey(
                        name: "fk_departamentos_grupos_grupo_id",
                        column: x => x.grupo_id,
                        principalSchema: "centros_costo",
                        principalTable: "grupos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departamentos_sucursales_ceco_sucursal_centro_costo_id",
                        column: x => x.sucursal_centro_costo_id,
                        principalSchema: "centros_costo",
                        principalTable: "sucursales_ceco",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "equipos",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subgrupo_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_equipos", x => x.id);
                    table.CheckConstraint("ck_equipos_estatus", "estatus BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_equipos_departamentos_departamento_id",
                        column: x => x.departamento_id,
                        principalSchema: "centros_costo",
                        principalTable: "departamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_equipos_subgrupos_subgrupo_id",
                        column: x => x.subgrupo_id,
                        principalSchema: "centros_costo",
                        principalTable: "subgrupos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_estatus",
                schema: "centros_costo",
                table: "departamentos",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_grupo_id",
                schema: "centros_costo",
                table: "departamentos",
                column: "grupo_id");

            migrationBuilder.CreateIndex(
                name: "ix_departamentos_sucursal_centro_costo_id",
                schema: "centros_costo",
                table: "departamentos",
                column: "sucursal_centro_costo_id");

            migrationBuilder.CreateIndex(
                name: "ux_departamentos_clave",
                schema: "centros_costo",
                table: "departamentos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_equipos_departamento_id",
                schema: "centros_costo",
                table: "equipos",
                column: "departamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_equipos_estatus",
                schema: "centros_costo",
                table: "equipos",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_equipos_subgrupo_id",
                schema: "centros_costo",
                table: "equipos",
                column: "subgrupo_id");

            migrationBuilder.CreateIndex(
                name: "ux_equipos_clave",
                schema: "centros_costo",
                table: "equipos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grupos_estatus",
                schema: "centros_costo",
                table: "grupos",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ux_grupos_nombre",
                schema: "centros_costo",
                table: "grupos",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subgrupos_estatus",
                schema: "centros_costo",
                table: "subgrupos",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ux_subgrupos_nombre",
                schema: "centros_costo",
                table: "subgrupos",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_ceco_estatus",
                schema: "centros_costo",
                table: "sucursales_ceco",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ux_sucursales_ceco_clave_ceco",
                schema: "centros_costo",
                table: "sucursales_ceco",
                column: "clave_ceco",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sucursales_ceco_sucursal_id",
                schema: "centros_costo",
                table: "sucursales_ceco",
                column: "sucursal_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipos",
                schema: "centros_costo");

            migrationBuilder.DropTable(
                name: "departamentos",
                schema: "centros_costo");

            migrationBuilder.DropTable(
                name: "subgrupos",
                schema: "centros_costo");

            migrationBuilder.DropTable(
                name: "grupos",
                schema: "centros_costo");

            migrationBuilder.DropTable(
                name: "sucursales_ceco",
                schema: "centros_costo");
        }
    }
}
