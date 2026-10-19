using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorOtr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otr_envios",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_origen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_aplicada = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contenido_json = table.Column<string>(type: "jsonb", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contenedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    revision_contenido = table.Column<int>(type: "integer", nullable: false),
                    ediciones_json = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otr_envios", x => x.id);
                    table.ForeignKey(
                        name: "fk_otr_envios_contenedores_contenedor_id",
                        column: x => x.contenedor_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "contenedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_otr_envios_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "otr_conflictos",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    otr_envio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_origen = table.Column<long>(type: "bigint", nullable: false),
                    campo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    valor_otr = table.Column<string>(type: "text", nullable: false),
                    valor_erp = table.Column<string>(type: "text", nullable: false),
                    actor_erp = table.Column<string>(type: "text", nullable: false),
                    fecha_erp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recibido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelto_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usar_otr = table.Column<bool>(type: "boolean", nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otr_conflictos", x => x.id);
                    table.ForeignKey(
                        name: "fk_otr_conflictos_otr_envios_otr_envio_id",
                        column: x => x.otr_envio_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "otr_envios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "otr_lineas",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    otr_envio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_origen = table.Column<long>(type: "bigint", nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    partida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otr_lineas", x => x.id);
                    table.ForeignKey(
                        name: "fk_otr_lineas_otr_envios_otr_envio_id",
                        column: x => x.otr_envio_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "otr_envios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_otr_lineas_partidas_embarcadas_partida_id",
                        column: x => x.partida_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "partidas_embarcadas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "otr_salidas",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    otr_envio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    valor_json = table.Column<string>(type: "jsonb", nullable: false),
                    version_erp = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_error = table.Column<string>(type: "text", nullable: true),
                    ultimo_intento_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acuse_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acuse_resultado = table.Column<string>(type: "text", nullable: true),
                    acuse_motivo = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otr_salidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_otr_salidas_otr_envios_otr_envio_id",
                        column: x => x.otr_envio_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "otr_envios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_otr_conflictos_otr_envio_id_version_origen_campo",
                schema: "comercio_exterior",
                table: "otr_conflictos",
                columns: new[] { "otr_envio_id", "version_origen", "campo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otr_envios_contenedor_id",
                schema: "comercio_exterior",
                table: "otr_envios",
                column: "contenedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_otr_envios_empresa_id_id_origen",
                schema: "comercio_exterior",
                table: "otr_envios",
                columns: new[] { "empresa_id", "id_origen" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otr_envios_empresa_id_sucursal_id_estado",
                schema: "comercio_exterior",
                table: "otr_envios",
                columns: new[] { "empresa_id", "sucursal_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_otr_envios_expediente_id",
                schema: "comercio_exterior",
                table: "otr_envios",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_otr_lineas_otr_envio_id_version_origen_numero_pedido_posici",
                schema: "comercio_exterior",
                table: "otr_lineas",
                columns: new[] { "otr_envio_id", "version_origen", "numero_pedido", "posicion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otr_lineas_partida_id",
                schema: "comercio_exterior",
                table: "otr_lineas",
                column: "partida_id");

            migrationBuilder.CreateIndex(
                name: "ix_otr_salidas_empresa_id_estado",
                schema: "comercio_exterior",
                table: "otr_salidas",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_otr_salidas_otr_envio_id_tipo_version_erp",
                schema: "comercio_exterior",
                table: "otr_salidas",
                columns: new[] { "otr_envio_id", "tipo", "version_erp" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otr_conflictos",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "otr_lineas",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "otr_salidas",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "otr_envios",
                schema: "comercio_exterior");
        }
    }
}
