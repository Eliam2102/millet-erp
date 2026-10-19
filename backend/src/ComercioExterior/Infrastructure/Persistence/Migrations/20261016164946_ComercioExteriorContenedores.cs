using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorContenedores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clave_pedimento",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "documento_pedimento_exportacion_id",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_pedimento",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_pedimento_exportacion",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_pedimento",
                schema: "comercio_exterior",
                table: "documentos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contenedores",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sello = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    booking = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bl_guia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ruta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_carga_prevista = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_embarque_real = table.Column<DateOnly>(type: "date", nullable: true),
                    peso_kg = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    bultos = table.Column<int>(type: "integer", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    motivo_cancelacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contenedores", x => x.id);
                    table.ForeignKey(
                        name: "fk_contenedores_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vinculos_pedido_expediente",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vinculos_pedido_expediente", x => x.id);
                    table.ForeignKey(
                        name: "fk_vinculos_pedido_expediente_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partidas_embarcadas",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contenedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_origen_asignada = table.Column<long>(type: "bigint", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    confirmado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partidas_embarcadas", x => x.id);
                    table.ForeignKey(
                        name: "fk_partidas_embarcadas_contenedores_contenedor_id",
                        column: x => x.contenedor_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "contenedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_partidas_embarcadas_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contenedores_expediente_id",
                schema: "comercio_exterior",
                table: "contenedores",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_partidas_embarcadas_contenedor_id",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                column: "contenedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_partidas_embarcadas_empresa_id_pedido_facturable_id_posicion",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                columns: new[] { "empresa_id", "pedido_facturable_id", "posicion" });

            migrationBuilder.CreateIndex(
                name: "ix_partidas_embarcadas_expediente_id",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_vinculos_pedido_expediente_expediente_id_pedido_facturable_",
                schema: "comercio_exterior",
                table: "vinculos_pedido_expediente",
                columns: new[] { "expediente_id", "pedido_facturable_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ce_pedido_expediente_abierto",
                schema: "comercio_exterior",
                table: "vinculos_pedido_expediente",
                columns: new[] { "empresa_id", "pedido_facturable_id" },
                unique: true,
                filter: "activo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partidas_embarcadas",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "vinculos_pedido_expediente",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "contenedores",
                schema: "comercio_exterior");

            migrationBuilder.DropColumn(
                name: "clave_pedimento",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "documento_pedimento_exportacion_id",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "fecha_pedimento",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "numero_pedimento_exportacion",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "numero_pedimento",
                schema: "comercio_exterior",
                table: "documentos");
        }
    }
}
