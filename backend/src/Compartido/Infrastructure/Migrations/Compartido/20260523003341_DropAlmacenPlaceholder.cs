using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class DropAlmacenPlaceholder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los DbContexts se migran por proyecto, no en orden cronologico
            // global. En una base nueva Compartido llega aqui antes de que
            // Almacen haya copiado el placeholder. Solo se elimina cuando el
            // catalogo destino ya existe; de lo contrario Almacen completa la
            // transferencia y elimina el placeholder en su propia migracion.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF to_regclass('almacen.almacenes') IS NOT NULL THEN
                        DROP TABLE compartido.almacenes;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "almacenes",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
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
        }
    }
}
