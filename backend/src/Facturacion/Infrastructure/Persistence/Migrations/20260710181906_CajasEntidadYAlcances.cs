using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CajasEntidadYAlcances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caja",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
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
                    table.PrimaryKey("pk_caja", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuario_alcance",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canal_venta_id = table.Column<short>(type: "smallint", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_alcance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "caja_canal",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal_venta_id = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_canal", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_canal_cajas_caja_id",
                        column: x => x.caja_id,
                        principalSchema: "facturacion",
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "caja_sucursal",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_sucursal", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_sucursal_cajas_caja_id",
                        column: x => x.caja_id,
                        principalSchema: "facturacion",
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "caja_usuario",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_usuario", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_usuario_cajas_caja_id",
                        column: x => x.caja_id,
                        principalSchema: "facturacion",
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_caja_empresa_nombre",
                schema: "facturacion",
                table: "caja",
                columns: new[] { "empresa_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caja_canal_canal",
                schema: "facturacion",
                table: "caja_canal",
                column: "canal_venta_id");

            migrationBuilder.CreateIndex(
                name: "ux_caja_canal",
                schema: "facturacion",
                table: "caja_canal",
                columns: new[] { "caja_id", "canal_venta_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caja_sucursal_sucursal",
                schema: "facturacion",
                table: "caja_sucursal",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ux_caja_sucursal",
                schema: "facturacion",
                table: "caja_sucursal",
                columns: new[] { "caja_id", "sucursal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_caja_usuario_usuario",
                schema: "facturacion",
                table: "caja_usuario",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_caja_usuario",
                schema: "facturacion",
                table: "caja_usuario",
                columns: new[] { "caja_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_alcance_usuario",
                schema: "facturacion",
                table: "usuario_alcance",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_usuario_alcance",
                schema: "facturacion",
                table: "usuario_alcance",
                columns: new[] { "empresa_id", "usuario_id", "sucursal_id", "canal_venta_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caja_canal",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja_sucursal",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja_usuario",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "usuario_alcance",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja",
                schema: "facturacion");
        }
    }
}
