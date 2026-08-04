using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UbicacionesN4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ubicaciones",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_ubicaciones", x => x.id);
                    table.CheckConstraint("ck_ubicaciones_estatus", "estatus BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_ubicaciones_sub_almacenes_sub_almacen_id",
                        column: x => x.sub_almacen_id,
                        principalSchema: "almacen",
                        principalTable: "sub_almacenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ubicaciones_estatus",
                schema: "almacen",
                table: "ubicaciones",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_ubicaciones_sub_almacen_id",
                schema: "almacen",
                table: "ubicaciones",
                column: "sub_almacen_id");

            migrationBuilder.CreateIndex(
                name: "ux_ubicaciones_sub_almacen_clave",
                schema: "almacen",
                table: "ubicaciones",
                columns: new[] { "sub_almacen_id", "clave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ubicaciones",
                schema: "almacen");
        }
    }
}
