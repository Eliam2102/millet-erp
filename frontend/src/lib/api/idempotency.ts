import { useCallback, useEffect, useRef, useState } from 'react';
import { esIdempotencyFalloPrevio } from '@/lib/api/error';
import { queryClient } from '@/lib/query-client';

/**
 * Helpers de <c>Idempotency-Key</c> (ADR-0020).
 *
 * <para>Una key protege UNA operación lógica: mientras la operación no
 * termina bien, reintentos (error de red, doble clic, retry de
 * <c>IDEMPOTENCY_IN_PROGRESS</c>) reusan la key y el backend no duplica el
 * efecto (<c>core.idempotency_keys</c> devuelve la respuesta cacheada).</para>
 *
 * <para>Revisión 2026-09: en cuanto la mutación que llevaba la key termina
 * con éxito (o el backend responde <c>IDEMPOTENCY_PREVIOUS_FAILURE</c>), el
 * hook genera una key nueva automáticamente. Así un componente que sigue
 * montado (lista, página de detalle, fila editable) puede repetir la acción
 * — otro registro, o el mismo otra vez — sin recibir la respuesta de la
 * operación anterior.</para>
 *
 * <para>Ver doc 05 §7.6 para la lista de endpoints que requieren el
 * header.</para>
 */

/**
 * Busca <paramref name="key"/> en las variables de una mutación: el valor
 * directo o una propiedad de primer o segundo nivel
 * (<c>{ id, idempotencyKey }</c>, <c>{ command, idempotencyKey }</c>,
 * <c>{ args: { idempotencyKey } }</c>).
 */
function variablesUsanKey(variables: unknown, key: string): boolean {
  if (variables === key) return true;
  if (typeof variables !== 'object' || variables === null) return false;
  return Object.values(variables).some(
    (valor) =>
      valor === key ||
      (typeof valor === 'object' &&
        valor !== null &&
        Object.values(valor).some((anidado) => anidado === key)),
  );
}

/**
 * Llama <paramref name="rotar"/> cuando termina una mutación que usó la key
 * vigente: con éxito (la operación quedó hecha; la siguiente es otra) o con
 * <c>IDEMPOTENCY_PREVIOUS_FAILURE</c> (el backend ya no aceptará esa key).
 * Otros errores conservan la key para que el reintento sea seguro.
 */
function useRotarTrasExito(keyVigente: () => string | null, rotar: () => void): void {
  const keyRef = useRef(keyVigente);
  const rotarRef = useRef(rotar);
  // Siempre la versión del último render (la suscripción se crea una vez).
  useEffect(() => {
    keyRef.current = keyVigente;
    rotarRef.current = rotar;
  });

  useEffect(
    () =>
      queryClient.getMutationCache().subscribe((event) => {
        if (event.type !== 'updated') return;
        const termino =
          event.action.type === 'success' ||
          (event.action.type === 'error' && esIdempotencyFalloPrevio(event.action.error));
        if (!termino) return;
        const key = keyRef.current();
        if (key !== null && variablesUsanKey(event.mutation.state.variables, key)) {
          rotarRef.current();
        }
      }),
    [],
  );
}

/**
 * Key para formularios y acciones: estable entre re-renders y reintentos
 * de la misma operación, y nueva en cuanto esa operación termina con éxito
 * (ver <c>useRotarTrasExito</c>). Sirve igual para un sheet de creación que
 * para una lista con acciones por fila.
 *
 * <para>Si el mismo componente puede reenviar la operación con un body
 * distinto <b>antes</b> de que termine bien (corregir tras un rechazo que
 * mueve dinero), usa <c>useBodyScopedIdempotencyKey</c>.</para>
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
  const [key, setKey] = useState<string>(() => crypto.randomUUID());
  useRotarTrasExito(
    () => key,
    () => setKey(crypto.randomUUID()),
  );
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
 * Tras un éxito la key se renueva (ver <c>useRotarTrasExito</c>), igual
 * que en <c>useFormIdempotencyKey</c>, que basta cuando el body no se
 * corrige entre intentos.
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
  // Tras un éxito el siguiente keyFor(body) genera key nueva aunque el body
  // sea idéntico: repetir la misma operación a propósito es otra operación.
  useRotarTrasExito(
    () => ref.current?.key ?? null,
    () => {
      ref.current = null;
    },
  );
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
