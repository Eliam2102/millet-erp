using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdjuntosTiposDocOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tipos_documento_oc",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    obligatorio_si_importacion = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipos_documento_oc", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orden_compra_adjuntos",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    blob_url = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    fecha_carga = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_carga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orden_compra_adjuntos", x => x.id);
                    table.CheckConstraint("ck_oc_adjuntos_tamano_pos", "tamano_bytes > 0");
                    table.ForeignKey(
                        name: "fk_oc_adjuntos_tipo_documento",
                        column: x => x.tipo_documento_id,
                        principalSchema: "compras",
                        principalTable: "tipos_documento_oc",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orden_compra_adjuntos_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "compras",
                table: "tipos_documento_oc",
                columns: new[] { "id", "activo", "clave", "created_at", "created_by", "deleted_at", "descripcion", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000003-0003-0010-0000-000000000001"), true, "cotizacion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cotización del proveedor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            migrationBuilder.InsertData(
                schema: "compras",
                table: "tipos_documento_oc",
                columns: new[] { "id", "activo", "clave", "created_at", "created_by", "deleted_at", "descripcion", "obligatorio_si_importacion", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000003-0003-0010-0000-000000000002"), true, "ficha_tecnica", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Ficha técnica del material", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            migrationBuilder.InsertData(
                schema: "compras",
                table: "tipos_documento_oc",
                columns: new[] { "id", "activo", "clave", "created_at", "created_by", "deleted_at", "descripcion", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000003-0003-0010-0000-000000000003"), true, "correo_autorizacion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Correo o documento de autorización", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000003-0003-0010-0000-000000000004"), true, "pedimento", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Pedimento de importación", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000003-0003-0010-0000-000000000005"), true, "factura_proveedor_extranjero", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Factura del proveedor extranjero", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000003-0003-0010-0000-000000000006"), true, "packing_list", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Packing list", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000003-0003-0010-0000-000000000007"), true, "otro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Otro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_oc_adjuntos_oc",
                schema: "compras",
                table: "orden_compra_adjuntos",
                column: "orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_oc_adjuntos_tipo",
                schema: "compras",
                table: "orden_compra_adjuntos",
                column: "tipo_documento_id");

            migrationBuilder.CreateIndex(
                name: "uq_tipos_documento_oc_clave",
                schema: "compras",
                table: "tipos_documento_oc",
                column: "clave",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orden_compra_adjuntos",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "tipos_documento_oc",
                schema: "compras");
        }
    }
}
