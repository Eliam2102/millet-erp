using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0047 C7.2a — el trigger de saldos aprende a enrutar por bin
    /// explícito. Si la línea trae <c>ubicacion_id</c>, el saldo aterriza ahí
    /// (previa validación); si viene NULL, se conserva el enrutamiento a la
    /// ubicación default (ÚNICA) del sub-almacén — el path de compatibilidad
    /// mientras los flujos migran (C7.2b entradas, C7.2c salidas).
    ///
    /// <para><b>Validaciones del path explícito</b> (todas ERRCODE 23514):
    /// <list type="bullet">
    ///   <item><c>UBICACION_NO_PERTENECE_AL_SUBALMACEN</c> — la ubicación debe
    ///   ser del sub-almacén del movimiento (protege el ledger de descuadres
    ///   silenciosos).</item>
    ///   <item><c>ENTRADA_A_UBICACION_UNICA</c> — las entradas exigen ubicación
    ///   real; la ÚNICA (<c>es_default</c>) solo se drena por salidas y muere
    ///   sola (modelo de negocio C7.2).</item>
    ///   <item><c>UBICACION_INACTIVA</c> — una entrada no puede ir a un bin
    ///   inactivo. Solo entradas: un bin inactivo está vacío por el guardrail
    ///   UBICACION_EN_USO_CON_SALDO de C7.1, así que una salida de bin
    ///   inactivo muere sola en SALDO_INEXISTENTE.</item>
    /// </list></para>
    ///
    /// <para>El upsert de entradas (promedio ponderado) y los guards de salida
    /// (FOR UPDATE + SALDO_INEXISTENTE / SALDO_INSUFICIENTE, por bin) no
    /// cambian. La validación de asignación-en-entrada (ENTRADA_SIN_ASIGNACION)
    /// NO vive aquí: es política de negocio y va en los handlers (C7.2b),
    /// patrón REORDEN_SIN_ASIGNACION. Down restaura verbatim el cuerpo
    /// vigente anterior (el de QuitarReservas/PR4 — que ya no maneja
    /// <c>cantidad_reservada</c>; molde de versionado: SaldosNivel4).</para>
    /// </summary>
    public partial class TriggerBinExplicito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_ubicacion_id UUID;
    v_ubi_sub_almacen_id UUID;
    v_ubi_es_default BOOLEAN;
    v_ubi_estatus SMALLINT;
    v_es_entrada BOOLEAN;
    v_delta NUMERIC(14,4);
    v_existing_cantidad NUMERIC(14,4);
BEGIN
    -- Lookup del movimiento padre. Solo procesamos si está Registrado.
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    -- Enrutamiento a Nivel 4 (C7.2a): bin explícito de la línea si viene;
    -- NULL → la ubicación default (ÚNICA) del sub-almacén del movimiento
    -- (compatibilidad mientras los flujos migran en C7.2b/C7.2c).
    IF NEW.ubicacion_id IS NOT NULL THEN
        -- La existencia la garantiza la FK; el SELECT trae los atributos.
        SELECT u.sub_almacen_id, u.es_default, u.estatus
          INTO v_ubi_sub_almacen_id, v_ubi_es_default, v_ubi_estatus
          FROM almacen.ubicaciones u
         WHERE u.id = NEW.ubicacion_id;

        IF v_ubi_sub_almacen_id IS DISTINCT FROM v_sub_almacen_id THEN
            RAISE EXCEPTION
                'UBICACION_NO_PERTENECE_AL_SUBALMACEN: ubicacion=% es de sub_almacen=% pero el movimiento es de sub_almacen=%.',
                NEW.ubicacion_id, v_ubi_sub_almacen_id, v_sub_almacen_id
                USING ERRCODE = '23514';
        END IF;

        IF v_es_entrada AND v_ubi_es_default THEN
            RAISE EXCEPTION
                'ENTRADA_A_UBICACION_UNICA: ubicacion=% es la default (ÚNICA) de sub_almacen=% — las entradas exigen ubicación real; la ÚNICA solo se drena.',
                NEW.ubicacion_id, v_sub_almacen_id
                USING ERRCODE = '23514';
        END IF;

        IF v_es_entrada AND v_ubi_estatus IS DISTINCT FROM 0 THEN
            RAISE EXCEPTION
                'UBICACION_INACTIVA: ubicacion=% no está activa (estatus=%) — no puede recibir entradas.',
                NEW.ubicacion_id, v_ubi_estatus
                USING ERRCODE = '23514';
        END IF;

        v_ubicacion_id := NEW.ubicacion_id;
    ELSE
        SELECT u.id INTO v_ubicacion_id
          FROM almacen.ubicaciones u
         WHERE u.sub_almacen_id = v_sub_almacen_id
           AND u.es_default
         LIMIT 1;

        IF v_ubicacion_id IS NULL THEN
            RAISE EXCEPTION
                'UBICACION_DEFAULT_INEXISTENTE: sub_almacen=% no tiene ubicación default (es_default).',
                v_sub_almacen_id
                USING ERRCODE = '23514';
        END IF;
    END IF;

    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            v_ubicacion_id, v_sub_almacen_id, NEW.articulo_id, v_delta,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id) DO UPDATE
        SET
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
        SELECT cantidad
          INTO v_existing_cantidad
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = v_ubicacion_id
           AND articulo_id = NEW.articulo_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% — no se puede aplicar salida (cantidad=%).',
                v_ubicacion_id, NEW.articulo_id, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% disponible=% solicitado=%.',
                v_ubicacion_id, NEW.articulo_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = v_ubicacion_id
           AND articulo_id = NEW.articulo_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaurar el trigger ANTERIOR (el de 20260703200235_QuitarReservas,
            // PR4: enruta SIEMPRE a la ubicación default del sub-almacén, ignora
            // NEW.ubicacion_id, sin cantidad_reservada) verbatim.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION almacen.fn_movimientos_actualizar_saldo()
RETURNS TRIGGER AS $$
DECLARE
    v_tipo SMALLINT;
    v_estado SMALLINT;
    v_sub_almacen_id UUID;
    v_ubicacion_id UUID;
    v_es_entrada BOOLEAN;
    v_delta NUMERIC(14,4);
    v_existing_cantidad NUMERIC(14,4);
BEGIN
    SELECT m.tipo, m.estado, m.sub_almacen_id
      INTO v_tipo, v_estado, v_sub_almacen_id
      FROM almacen.movimientos_inventario m
     WHERE m.id = NEW.movimiento_id;

    IF v_estado IS DISTINCT FROM 2 THEN
        RETURN NEW;
    END IF;

    SELECT u.id INTO v_ubicacion_id
      FROM almacen.ubicaciones u
     WHERE u.sub_almacen_id = v_sub_almacen_id
       AND u.es_default
     LIMIT 1;

    IF v_ubicacion_id IS NULL THEN
        RAISE EXCEPTION
            'UBICACION_DEFAULT_INEXISTENTE: sub_almacen=% no tiene ubicación default (es_default).',
            v_sub_almacen_id
            USING ERRCODE = '23514';
    END IF;

    v_es_entrada := v_tipo IN (0, 3, 5, 9);
    v_delta := NEW.cantidad;

    IF v_es_entrada THEN
        INSERT INTO almacen.saldos_inventario (
            ubicacion_id, sub_almacen_id, articulo_id, cantidad,
            costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id
        )
        VALUES (
            v_ubicacion_id, v_sub_almacen_id, NEW.articulo_id, v_delta,
            NEW.costo_unitario_mxn, NOW(), NEW.movimiento_id
        )
        ON CONFLICT (ubicacion_id, articulo_id) DO UPDATE
        SET
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
        SELECT cantidad
          INTO v_existing_cantidad
          FROM almacen.saldos_inventario
         WHERE ubicacion_id = v_ubicacion_id
           AND articulo_id = NEW.articulo_id
         FOR UPDATE;

        IF v_existing_cantidad IS NULL THEN
            RAISE EXCEPTION
                'SALDO_INEXISTENTE: ubicacion=% articulo=% — no se puede aplicar salida (cantidad=%).',
                v_ubicacion_id, NEW.articulo_id, v_delta
                USING ERRCODE = '23514';
        END IF;

        IF v_existing_cantidad < v_delta THEN
            RAISE EXCEPTION
                'SALDO_INSUFICIENTE: ubicacion=% articulo=% disponible=% solicitado=%.',
                v_ubicacion_id, NEW.articulo_id, v_existing_cantidad, v_delta
                USING ERRCODE = '23514';
        END IF;

        UPDATE almacen.saldos_inventario
           SET cantidad = cantidad - v_delta,
               ultima_actualizacion_at = NOW(),
               ultimo_movimiento_id = NEW.movimiento_id
         WHERE ubicacion_id = v_ubicacion_id
           AND articulo_id = NEW.articulo_id;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");
        }
    }
}
