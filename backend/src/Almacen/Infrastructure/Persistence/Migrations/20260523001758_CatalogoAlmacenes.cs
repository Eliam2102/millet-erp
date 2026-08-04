using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoAlmacenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "almacenes",
                schema: "almacen",
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
                });

            migrationBuilder.CreateTable(
                name: "sub_almacenes",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
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
                    table.PrimaryKey("pk_sub_almacenes", x => x.id);
                    table.CheckConstraint("ck_sub_almacenes_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_sub_almacenes_tipo", "tipo BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_sub_almacenes_almacenes_almacen_id",
                        column: x => x.almacen_id,
                        principalSchema: "almacen",
                        principalTable: "almacenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_clave",
                schema: "almacen",
                table: "almacenes",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_estatus",
                schema: "almacen",
                table: "almacenes",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_sucursal_id",
                schema: "almacen",
                table: "almacenes",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_almacenes_almacen_id",
                schema: "almacen",
                table: "sub_almacenes",
                column: "almacen_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_almacenes_estatus",
                schema: "almacen",
                table: "sub_almacenes",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_sub_almacenes_tipo",
                schema: "almacen",
                table: "sub_almacenes",
                column: "tipo");

            migrationBuilder.CreateIndex(
                name: "ux_sub_almacenes_almacen_clave",
                schema: "almacen",
                table: "sub_almacenes",
                columns: new[] { "almacen_id", "clave" },
                unique: true);

            // F1-PR1: copy-from aditivo desde el placeholder
            // `compartido.almacenes` (entidad MVP-light de DatosMaestros).
            // Preserva ids y todas las columnas relevantes para que los
            // consumidores externos (Compras, CxP, OrganizacionEndpoints)
            // sigan funcionando hasta que F1-PR2 haga el DROP del
            // placeholder y re-apunte los lectores al nuevo schema.
            //
            // Idempotente vía ON CONFLICT DO NOTHING: la migración la
            // aplica EF Core idempotent-script en CI, así que un
            // re-deploy no falla.
            migrationBuilder.Sql(@"
                INSERT INTO almacen.almacenes (
                    id, clave, nombre, sucursal_id, estatus,
                    version, created_at, updated_at, created_by, updated_by, deleted_at
                )
                SELECT
                    id, clave, nombre, sucursal_id, estatus,
                    version, created_at, updated_at, created_by, updated_by, deleted_at
                FROM compartido.almacenes
                ON CONFLICT (id) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sub_almacenes",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "almacenes",
                schema: "almacen");
        }
    }
}
