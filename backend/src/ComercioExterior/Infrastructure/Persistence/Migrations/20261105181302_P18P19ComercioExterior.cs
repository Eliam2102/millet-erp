using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P18P19ComercioExterior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_incidencias_expediente_id",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropIndex(
                name: "ix_avisos_incidencia_incidencia_id",
                schema: "comercio_exterior",
                table: "avisos_incidencia");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "aduanales_revisado_en",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "aduanales_revisado_por_id",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bultos",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fraccion_arancelaria",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "peso_kg",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unidad_aduana",
                schema: "comercio_exterior",
                table: "partidas_embarcadas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_compromiso_anterior",
                schema: "comercio_exterior",
                table: "incidencias_historial",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_compromiso_nueva",
                schema: "comercio_exterior",
                table: "incidencias_historial",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "siguiente_accion_anterior",
                schema: "comercio_exterior",
                table: "incidencias_historial",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "siguiente_accion_nueva",
                schema: "comercio_exterior",
                table: "incidencias_historial",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ajuste_autorizado_en",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ajuste_autorizado_por_id",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "automatica",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_anterior",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_nueva",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creado_por_id",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "diferencia_cantidad",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_compromiso",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "siguiente_accion",
                schema: "comercio_exterior",
                table: "incidencias",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "impuesto_peca",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cierre_aprobado_en",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cierre_aprobado_por_id",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cierre_documental_en",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cierre_documental_por_id",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cierre_financiero_en",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cierre_financiero_por_id",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cierre_fisico_en",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cierre_fisico_por_id",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preparado_por_id",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "preparadores_embarque",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<int>(
                name: "version_embarque",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "modalidad",
                schema: "comercio_exterior",
                table: "contenedores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_vencimiento",
                schema: "comercio_exterior",
                table: "avisos_incidencia",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "comprobado_por",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "depositos_recibidos",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "devolucion_aprobada",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "devolucion_pendiente_id",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "historial_comprobaciones",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "importe_solicitado",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pagado_mxn",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "pago_id",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pagos_recibidos",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "soporte_calculo_id",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "autorizaciones_salida",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_embarque = table.Column<int>(type: "integer", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    autorizada = table.Column<bool>(type: "boolean", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contenido_json = table.Column<string>(type: "jsonb", nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizaciones_salida", x => x.id);
                    table.ForeignKey(
                        name: "fk_autorizaciones_salida_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuracion",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dias_habiles_incidencia_recepcion = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facturas",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_embarque = table.Column<int>(type: "integer", nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    folio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fecha_timbrado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelada = table.Column<bool>(type: "boolean", nullable: false),
                    uuid_original = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    actualizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas", x => x.id);
                    table.ForeignKey(
                        name: "fk_facturas_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partidas_importacion",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contenedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    oc_linea_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cantidad_recibida = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    fecha_recepcion = table.Column<DateOnly>(type: "date", nullable: true),
                    compromiso_recepcion = table.Column<DateOnly>(type: "date", nullable: true),
                    eventos_recepcion = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    registrado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partidas_importacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_partidas_importacion_contenedores_contenedor_id",
                        column: x => x.contenedor_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "contenedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_partidas_importacion_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalSchema: "comercio_exterior",
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requisitos_hitos",
                schema: "comercio_exterior",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    tipo_documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hito_clave = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisitos_hitos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_expediente_id_partida_id",
                schema: "comercio_exterior",
                table: "incidencias",
                columns: new[] { "expediente_id", "partida_id" },
                unique: true,
                filter: "automatica AND estado IN ('Abierta', 'EnAtencion')");

            migrationBuilder.CreateIndex(
                name: "ix_gastos_expediente_empresa_id_factura_proveedor_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                columns: new[] { "empresa_id", "factura_proveedor_id" },
                unique: true,
                filter: "factura_proveedor_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_incidencia_incidencia_id_es_vencimiento",
                schema: "comercio_exterior",
                table: "avisos_incidencia",
                columns: new[] { "incidencia_id", "es_vencimiento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_autorizaciones_salida_expediente_id_secuencia",
                schema: "comercio_exterior",
                table: "autorizaciones_salida",
                columns: new[] { "expediente_id", "secuencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_empresa_id",
                schema: "comercio_exterior",
                table: "configuracion",
                column: "empresa_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturas_expediente_id",
                schema: "comercio_exterior",
                table: "facturas",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_partidas_importacion_contenedor_id",
                schema: "comercio_exterior",
                table: "partidas_importacion",
                column: "contenedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_partidas_importacion_expediente_id",
                schema: "comercio_exterior",
                table: "partidas_importacion",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisitos_hitos_empresa_id_tipo_tipo_documento_id",
                schema: "comercio_exterior",
                table: "requisitos_hitos",
                columns: new[] { "empresa_id", "tipo", "tipo_documento_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizaciones_salida",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "configuracion",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "facturas",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "partidas_importacion",
                schema: "comercio_exterior");

            migrationBuilder.DropTable(
                name: "requisitos_hitos",
                schema: "comercio_exterior");

            migrationBuilder.DropIndex(
                name: "ix_incidencias_expediente_id_partida_id",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropIndex(
                name: "ix_gastos_expediente_empresa_id_factura_proveedor_id",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropIndex(
                name: "ix_avisos_incidencia_incidencia_id_es_vencimiento",
                schema: "comercio_exterior",
                table: "avisos_incidencia");

            migrationBuilder.DropColumn(
                name: "aduanales_revisado_en",
                schema: "comercio_exterior",
                table: "partidas_embarcadas");

            migrationBuilder.DropColumn(
                name: "aduanales_revisado_por_id",
                schema: "comercio_exterior",
                table: "partidas_embarcadas");

            migrationBuilder.DropColumn(
                name: "bultos",
                schema: "comercio_exterior",
                table: "partidas_embarcadas");

            migrationBuilder.DropColumn(
                name: "fraccion_arancelaria",
                schema: "comercio_exterior",
                table: "partidas_embarcadas");

            migrationBuilder.DropColumn(
                name: "peso_kg",
                schema: "comercio_exterior",
                table: "partidas_embarcadas");

            migrationBuilder.DropColumn(
                name: "unidad_aduana",
                schema: "comercio_exterior",
                table: "partidas_embarcadas");

            migrationBuilder.DropColumn(
                name: "fecha_compromiso_anterior",
                schema: "comercio_exterior",
                table: "incidencias_historial");

            migrationBuilder.DropColumn(
                name: "fecha_compromiso_nueva",
                schema: "comercio_exterior",
                table: "incidencias_historial");

            migrationBuilder.DropColumn(
                name: "siguiente_accion_anterior",
                schema: "comercio_exterior",
                table: "incidencias_historial");

            migrationBuilder.DropColumn(
                name: "siguiente_accion_nueva",
                schema: "comercio_exterior",
                table: "incidencias_historial");

            migrationBuilder.DropColumn(
                name: "ajuste_autorizado_en",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "ajuste_autorizado_por_id",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "automatica",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "cantidad_anterior",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "cantidad_nueva",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "creado_por_id",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "diferencia_cantidad",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "fecha_compromiso",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "siguiente_accion",
                schema: "comercio_exterior",
                table: "incidencias");

            migrationBuilder.DropColumn(
                name: "impuesto_peca",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "cierre_aprobado_en",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_aprobado_por_id",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_documental_en",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_documental_por_id",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_financiero_en",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_financiero_por_id",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_fisico_en",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "cierre_fisico_por_id",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "preparado_por_id",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "preparadores_embarque",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "version_embarque",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "modalidad",
                schema: "comercio_exterior",
                table: "contenedores");

            migrationBuilder.DropColumn(
                name: "es_vencimiento",
                schema: "comercio_exterior",
                table: "avisos_incidencia");

            migrationBuilder.DropColumn(
                name: "comprobado_por",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "depositos_recibidos",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "devolucion_aprobada",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "devolucion_pendiente_id",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "historial_comprobaciones",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "importe_solicitado",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "pagado_mxn",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "pago_id",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "pagos_recibidos",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "soporte_calculo_id",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_expediente_id",
                schema: "comercio_exterior",
                table: "incidencias",
                column: "expediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_incidencia_incidencia_id",
                schema: "comercio_exterior",
                table: "avisos_incidencia",
                column: "incidencia_id",
                unique: true);
        }
    }
}
