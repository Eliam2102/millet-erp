import { useCallback, useRef, useState } from 'react';

/**
 * Helpers de <c>Idempotency-Key</c> (ADR-0020).
 *
 * <para>Cada formulario que dispara una mutation con efecto de "crear/
 * transmitir/autorizar/etc." necesita un key estable durante la vida
 * del componente, para que reintentos tras error de red o retry de
 * <c>IDEMPOTENCY_IN_PROGRESS</c> NO dupliquen el efecto en el backend
 * (la tabla <c>core.idempotency_log</c> garantiza que el segundo POST
 * con el mismo key + body devuelva la respuesta cacheada).</para>
 *
 * <para>Si el form se desmonta y se vuelve a montar (navegación + back),
 * es un form "nuevo" → nueva key. Eso es coherente con el ADR-0020.</para>
 *
 * <para>Ver doc 05 §7.6 para la lista de endpoints que requieren el
 * header.</para>
 */

/**
 * Hook que retorna un UUID v4 estable durante la vida del componente.
 * Usa el initializer lazy de <c>useState</c> para que el UUID se genere
 * una sola vez por montaje (re-renders devuelven el mismo valor; un
 * remount = key nueva, coherente con ADR-0020).
 *
 * <para><b>Solo para forms de creación única (one-shot)</b>: un montaje =
 * una operación lógica. Para forms <b>multi-submit</b> que se mantienen
 * montados y disparan varias mutaciones con bodies distintos (p.ej. los
 * inline forms de "agregar línea" de RQ/OC) NO uses este hook: generá una
 * key fresca por submit con <c>crypto.randomUUID()</c> dentro del
 * <c>onSubmit</c> y pasala en las variables del <c>mutate</c> (patrón
 * <c>AccionesOC</c> / <c>SheetNuevaOC</c>). Reusar la misma key con un
 * body distinto = 422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY.</para>
 *
 * @example
 * ```tsx
 * function NuevaRequisicion() {
 *   const idempotencyKey = useFormIdempotencyKey();
 *   const crear = useCrearRequisicion();
 *   const onSubmit = (values) => crear.mutate({ values, idempotencyKey });
 *   ...
 * }
 * ```
 */
export function useFormIdempotencyKey(): string {
  const [key] = useState<string>(() => crypto.randomUUID());
  return key;
}

/**
 * Hook para acciones NO idempotentes que <b>mueven dinero y pueden
 * reintentarse</b> (registrar pago, registrar/confirmar ingreso, ligar pago
 * a cuenta, revertir pago). Devuelve <c>keyFor(body)</c>:
 *
 * <list type="bullet">
 *   <item>Mientras el <c>body</c> serialice IGUAL, retorna la MISMA key. Un
 *   reintento tras un error de RED (timeout/502 con el efecto ya aplicado en
 *   el backend) re-envía key+body idénticos → <c>core.idempotency_log</c>
 *   devuelve la respuesta cacheada en lugar de <b>duplicar el desembolso</b>.
 *   Es el bug que `crypto.randomUUID()` fresco por submit NO previene.</item>
 *   <item>Si el usuario CORRIGE el comando tras un rechazo de negocio (body
 *   distinto), retorna una key NUEVA, evitando el
 *   <c>422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY</c> que produciría una
 *   key estable por montaje (`useFormIdempotencyKey`).</item>
 * </list>
 *
 * Para creates one-shot que se desmontan al éxito basta
 * <c>useFormIdempotencyKey</c>; para acciones repetibles con guarda de
 * dominio (reintento de timbrado, cancelar cobro) usar
 * <c>crypto.randomUUID()</c> fresco por clic.
 *
 * El hash usa <c>JSON.stringify</c>: el orden de llaves es determinista
 * porque el comando se construye con el mismo literal en cada render.
 *
 * @example
 * ```tsx
 * const keyFor = useBodyScopedIdempotencyKey();
 * function confirmar() {
 *   const command = { cuentaBancariaId, monto, ... };
 *   registrar.mutate({ command, idempotencyKey: keyFor(command) });
 * }
 * ```
 */
export function useBodyScopedIdempotencyKey(): (body: unknown) => string {
  const ref = useRef<{ hash: string; key: string } | null>(null);
  return useCallback((body: unknown) => {
    const hash = JSON.stringify(body);
    if (ref.current === null || ref.current.hash !== hash) {
      ref.current = { hash, key: crypto.randomUUID() };
    }
    return ref.current.key;
  }, []);
}

/**
 * Construye un objeto plano <c>{ "Idempotency-Key": "..." }</c> que se
 * hace spread sobre los headers de una request. Si la key es
 * <c>undefined</c> devuelve <c>{}</c> (no se envía header).
 *
 * @example
 * ```ts
 * fetch(url, { headers: { 'Content-Type': 'application/json', ...idempotencyHeader(key) } });
 * ```
 */
export function idempotencyHeader(
  key: string | undefined | null,
): Record<string, string> {
  if (!key) return {};
  return { 'Idempotency-Key': key };
}
