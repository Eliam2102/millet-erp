using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovimientosYSaldos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folio_secuencias_movimiento",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefijo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    ultimo_numero = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_secuencias_movimiento", x => x.id);
                    table.CheckConstraint("ck_folio_secuencias_ultimo_no_negativo", "ultimo_numero >= 0");
                });

            migrationBuilder.CreateTable(
                name: "movimientos_inventario",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_movimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_registro = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    oc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oc_linea_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    packing_list_blob_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rq_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vale_blob_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    salida_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recepcion_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conteo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pendiente_regularizacion = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_limite_regularizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rq_regularizadora_id = table.Column<Guid>(type: "uuid", nullable: true),
                    persona_destinataria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    maquina_destino_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comentario_libre = table.Column<string>(type: "text", nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    estado_material = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    factura_id_origen_diff = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos_inventario", x => x.id);
                    table.CheckConstraint("ck_movimientos_estado", "estado BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_movimientos_tipo", "tipo BETWEEN 0 AND 9");
                    table.ForeignKey(
                        name: "fk_movimientos_inventario_sub_almacenes_sub_almacen_id",
                        column: x => x.sub_almacen_id,
                        principalSchema: "almacen",
                        principalTable: "sub_almacenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saldos_inventario",
                schema: "almacen",
                columns: table => new
                {
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    cantidad_reservada = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    cantidad_disponible = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false, computedColumnSql: "cantidad - cantidad_reservada", stored: true),
                    costo_promedio_mxn = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    valor_inventario_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, computedColumnSql: "cantidad * costo_promedio_mxn", stored: true),
                    ultima_actualizacion_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_movimiento_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saldos_inventario", x => new { x.sub_almacen_id, x.articulo_id });
                    table.CheckConstraint("ck_saldos_cantidad_no_negativa", "cantidad >= 0");
                    table.CheckConstraint("ck_saldos_reservada_no_excede", "cantidad_reservada <= cantidad");
                    table.CheckConstraint("ck_saldos_reservada_no_negativa", "cantidad_reservada >= 0");
                    table.ForeignKey(
                        name: "fk_saldos_inventario_sub_almacenes_sub_almacen_id",
                        column: x => x.sub_almacen_id,
                        principalSchema: "almacen",
                        principalTable: "sub_almacenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lineas_movimiento",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    costo_unitario_mxn = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    monto_total_mxn = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda_original = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tipo_cambio_aplicado = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    linea_factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    centro_costo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proyecto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cantidad_teorica_al_contar = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    cantidad_real_contada = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    ubicacion_referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    comentario_linea = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_movimiento", x => x.id);
                    table.CheckConstraint("ck_lineas_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("ck_lineas_costo_no_negativo", "costo_unitario_mxn >= 0");
                    table.ForeignKey(
                        name: "fk_lineas_movimiento_movimientos_inventario_movimiento_id",
                        column: x => x.movimiento_id,
                        principalSchema: "almacen",
                        principalTable: "movimientos_inventario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_folio_secuencias_prefijo_anio",
                schema: "almacen",
                table: "folio_secuencias_movimiento",
                columns: new[] { "prefijo", "anio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lineas_movimiento_articulo_id",
                schema: "almacen",
                table: "lineas_movimiento",
                column: "articulo_id");

            migrationBuilder.CreateIndex(
                name: "ix_lineas_movimiento_movimiento_id",
                schema: "almacen",
                table: "lineas_movimiento",
                column: "movimiento_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_por_oc",
                schema: "almacen",
                table: "movimientos_inventario",
                columns: new[] { "oc_id", "tipo" },
                filter: "oc_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_por_rq",
                schema: "almacen",
                table: "movimientos_inventario",
                columns: new[] { "rq_id", "tipo" },
                filter: "rq_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_recepciones",
                schema: "almacen",
                table: "movimientos_inventario",
                columns: new[] { "sub_almacen_id", "fecha_movimiento" },
                filter: "tipo = 0 AND estado = 2");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_vale_pendientes",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "fecha_limite_regularizacion",
                filter: "tipo = 2 AND pendiente_regularizacion = true");

            migrationBuilder.CreateIndex(
                name: "ux_movimientos_folio",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "folio",
                unique: true,
                filter: "folio IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_saldos_con_stock",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "articulo_id", "sub_almacen_id" },
                filter: "cantidad > 0");

            migrationBuilder.CreateIndex(
                name: "ix_saldos_sub_almacen",
                schema: "almacen",
                table: "saldos_inventario",
                columns: new[] { "sub_almacen_id", "articulo_id" });

            // ────────────────────────────────────────────────────────────────
            // F2-PR1: Trigger transaccional `tg_movimientos_actualizar_saldo`
            //
            // Cuidado §3.1 del 04-cuidados-infra: cuando un movimiento pasa a
            // estado=Registrado (2), el trigger BEFORE INSERT actualiza
            // `saldos_inventario` en la misma transacción que el INSERT del
            // movimiento. La actualización es UPSERT con costo promedio
            // ponderado en entradas (A3 del 01-diseno).
            //
            // Por qué BEFORE en lugar de AFTER:
            //   - Si validamos saldo negativo en BEFORE, podemos abortar la
            //     transacción ANTES de que el movimiento se materialice y la
            //     línea se inserte. Resultado: el INSERT del padre falla con
            //     un error claro, no un trigger AFTER que deja el padre con
            //     un saldo huérfano.
            //   - La validación del CHECK ck_saldos_cantidad_no_negativa
            //     actúa como segunda barrera (defense in depth).
            //
            // Por qué AFTER INSERT sobre `lineas_movimiento` (NO sobre
            // `movimientos_inventario`):
            //   - El padre se inserta SIN líneas todavía (EF Core inserta el
            //     padre primero, luego los hijos en la misma transacción).
            //     Si el trigger fuera sobre `movimientos_inventario`, no
            //     habría líneas para procesar.
            //   - Disparamos sobre `lineas_movimiento` para cada línea: el
            //     trigger consulta el `tipo` del movimiento padre, decide el
            //     signo, y aplica el delta.
            //
            // Idempotencia: el trigger se aplica UNA SOLA VEZ por línea, en
            // el INSERT inicial. Como las líneas son inmutables después de
            // Registrado (inmutabilidad del agregado), no hay UPDATEs que
            // requieran lógica de delta.
            //
            // Condición: el trigger SOLO procesa líneas cuyo movimiento padre
            // tiene estado=2 (Registrado). Las líneas de Borrador no afectan
            // saldo. La transición Borrador → Registrado en el handler hace
            // INSERT INTO lineas_movimiento dentro de la misma TX que el
            // UPDATE del estado del padre — el trigger ve estado=2.
            // ────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_es_entrada BOOLEAN;
    v_delta NUMERIC(14,4);
    v_existing_cantidad NUMERIC(14,4);
    v_existing_costo NUMERIC(14,4);
BEGIN
    -- Lookup del movimiento padre. Solo procesamos si está Registrado.
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        -- Borrador / Validado / Cancelado: no afectan saldo.
        RETURN NEW;
    END IF;

    -- Mapa de tipos a signo (alineado con TipoMovimientoExtensions.EsEntrada):
    --   0 EntradaCompra, 3 DevolucionSalida, 5 AjustePositivo,
    --   9 ReincorporacionTrasRevision → entrada (+)
    --   resto → salida (-)
    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    -- UPSERT en saldos_inventario.
    -- Para entradas: actualiza costo promedio ponderado A3.
    -- Para salidas: solo decrementa cantidad (costo no cambia).
    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            sub_almacen_id, articulo_id, cantidad, cantidad_reservada,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            v_sub_almacen_id, NEW.articulo_id, v_delta, 0,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (sub_almacen_id, articulo_id) DO UPDATE
        SET
            -- Costo promedio ponderado: ((c_anterior * cu_anterior) + (c_entrada * cu_entrada)) / (c_anterior + c_entrada).
            -- Si c_anterior es 0, el promedio nuevo = cu_entrada.
            costo_promedio_mxn = CASE
                WHEN saldos_inventario.cantidad + v_delta = 0 THEN NEW.costo_unitario_mxn
                ELSE ROUND(
                    ((saldos_inventario.cantidad * saldos_inventario.costo_promedio_mxn)
                     + (v_delta * NEW.costo_unitario_mxn))
                    / (saldos_inventario.cantidad + v_delta),
                    4
                )
            END,
            cantidad = saldos_inventario.cantidad + v_delta,
            ultima_actualizacion_at = NOW(),
            ultimo_movimiento_id = NEW.movimiento_id;
    ELSE
        -- Salida: valida saldo suficiente ANTES de actualizar. La fila DEBE existir
        -- (un artículo sin saldo no debió haber pasado las validaciones del handler;
        -- esto es la última barrera defensiva).
        SELECT cantidad, costo_promedio_mxn
          INTO v_existing_cantidad, v_existing_costo
          FROM almacen.saldos_inventario
         WHERE sub_almacen_id = v_sub_almacen_id
           AND articulo_id = NEW.articulo_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: sub_almacen=% articulo=% — no se puede aplicar salida (cantidad=%).',
                v_sub_almacen_id, NEW.articulo_id, v_delta
                USING ERRCODE = '23514';  -- check_violation
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: sub_almacen=% articulo=% disponible=% solicitado=%.',
                v_sub_almacen_id, NEW.articulo_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE sub_almacen_id = v_sub_almacen_id
           AND articulo_id = NEW.articulo_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER tg_movimientos_actualizar_saldo
AFTER INSERT ON almacen.lineas_movimiento
FOR EACH ROW
EXECUTE FUNCTION almacen.fn_movimientos_actualizar_saldo();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS tg_movimientos_actualizar_saldo ON almacen.lineas_movimiento;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS almacen.fn_movimientos_actualizar_saldo();");

            migrationBuilder.DropTable(
                name: "folio_secuencias_movimiento",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "lineas_movimiento",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "saldos_inventario",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "movimientos_inventario",
                schema: "almacen");
        }
    }
}
