using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportacionRecepcionCom06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recepciones_importacion",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_pedimento = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    numero_bl = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    numero_contenedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aduana = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    clave_pedimento = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepciones_importacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recepcion_importacion_partidas",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_oc_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fraccion_arancelaria = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    recepcion_importacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepcion_importacion_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_recepcion_importacion_partidas_recepciones_importacion_rece",
                        column: x => x.recepcion_importacion_id,
                        principalSchema: "almacen",
                        principalTable: "recepciones_importacion",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_recepcion_importacion_partidas_recepcion_importacion_id",
                schema: "almacen",
                table: "recepcion_importacion_partidas",
                column: "recepcion_importacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_importacion_empresa_id_numero_pedimento",
                schema: "almacen",
                table: "recepciones_importacion",
                columns: new[] { "empresa_id", "numero_pedimento" });

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_importacion_empresa_id_orden_compra_id",
                schema: "almacen",
                table: "recepciones_importacion",
                columns: new[] { "empresa_id", "orden_compra_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_importacion_recepcion_id",
                schema: "almacen",
                table: "recepciones_importacion",
                column: "recepcion_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recepcion_importacion_partidas",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "recepciones_importacion",
                schema: "almacen");
        }
    }
}
