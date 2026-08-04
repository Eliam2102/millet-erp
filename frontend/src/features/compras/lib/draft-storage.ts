import { useEffect, useRef, useState } from 'react';

/**
 * Persistencia local de drafts en <c>localStorage</c> para protección
 * anti-pérdida en captura (P4 nueva, P5 line dialog). Doc 05 §13.2.
 *
 * <para>Convención de keys: <c>compras:rq:draft:nueva:&lt;userId&gt;:&lt;empresaId&gt;</c>
 * para P4. Aislada por usuario + empresa para que distintos
 * SuperAdmins no se pisen drafts.</para>
 *
 * <para>Patrón:</para>
 * <list>
 *   <item><c>useDraft(key, current, opts)</c> — escribe el current al
 *   storage con debounce 500 ms.</item>
 *   <item><c>readDraft(key)</c> — lee al montar la pantalla; el caller
 *   muestra modal "Recuperar borrador" si retorna algo.</item>
 *   <item><c>clearDraft(key)</c> — borra tras submit exitoso.</item>
 * </list>
 *
 * <para>Errores de <c>localStorage</c> (cuota llena, modo privado en
 * iOS, storage deshabilitado) se silencian con try/catch — el draft
 * es nice-to-have, no bloqueante. Igual logueamos a consola para
 * diagnóstico.</para>
 */

const DEBOUNCE_MS = 500;

export interface DraftMeta {
  /** ISO timestamp UTC del último guardado. */
  savedAt: string;
}

export interface StoredDraft<T> {
  values: T;
  meta: DraftMeta;
}

/** Construye la key estándar para el draft de "Nueva requisición" (P4). */
export function buildNuevaRequisicionDraftKey(
  userId: string | null | undefined,
  empresaId: string | null | undefined,
): string | null {
  if (!userId || !empresaId) return null;
  return `compras:rq:draft:nueva:${userId}:${empresaId}`;
}

/**
 * Construye la key estándar para el draft de "Nueva orden de compra"
 * (P4 OC, UF2-PR1). Aislada por usuario + empresa para que distintos
 * compradores no se pisen drafts en la misma máquina.
 */
export function buildNuevaOrdenCompraDraftKey(
  userId: string | null | undefined,
  empresaId: string | null | undefined,
): string | null {
  if (!userId || !empresaId) return null;
  return `compras:oc:draft:nueva:${userId}:${empresaId}`;
}

/**
 * Lee un draft persistido. Devuelve <c>null</c> si no existe, está
 * corrupto, o <c>localStorage</c> falla.
 */
export function readDraft<T>(key: string): StoredDraft<T> | null {
  try {
    const raw = window.localStorage.getItem(key);
    if (raw == null) return null;
    const parsed = JSON.parse(raw) as StoredDraft<T>;
    if (
      parsed == null ||
      typeof parsed !== 'object' ||
      typeof parsed.meta?.savedAt !== 'string'
    ) {
      return null;
    }
    return parsed;
  } catch (e) {
    console.warn('[draft-storage] readDraft falló:', e);
    return null;
  }
}

/** Borra un draft (tras submit exitoso o "Descartar" en el modal). */
export function clearDraft(key: string): void {
  try {
    window.localStorage.removeItem(key);
  } catch (e) {
    console.warn('[draft-storage] clearDraft falló:', e);
  }
}

/**
 * Escribe el draft con debounce 500 ms. Cancela cualquier escritura
 * pendiente al desmontar el componente.
 */
function writeDraft<T>(key: string, values: T): void {
  try {
    const payload: StoredDraft<T> = {
      values,
      meta: { savedAt: new Date().toISOString() },
    };
    window.localStorage.setItem(key, JSON.stringify(payload));
  } catch (e) {
    console.warn('[draft-storage] writeDraft falló:', e);
  }
}

/**
 * <c>useDraftPersist(key, values, enabled)</c> — sincroniza
 * <c>values</c> al <c>localStorage</c> con debounce 500 ms cada vez
 * que cambia. Cuando <c>enabled=false</c> (form en estado pristine,
 * o submit en progreso), suspende la persistencia sin borrar el
 * draft existente.
 *
 * <para>Doc 05 §13.2: complementa
 * <c>&lt;useUnsavedChangesGuard isDirty&gt;</c>; el guard usa el
 * <c>beforeunload</c> nativo del browser para evitar que el usuario
 * cierre la tab; este hook persiste para que si la tab muere igual
 * (crash, kill task), el draft sobreviva.</para>
 *
 * @example
 * ```tsx
 * useDraftPersist(draftKey, form.watch(), form.formState.isDirty);
 * ```
 */
export function useDraftPersist<T>(
  key: string | null | undefined,
  values: T,
  enabled: boolean,
): void {
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (key == null || !enabled) return;

    timerRef.current = setTimeout(() => {
      writeDraft(key, values);
    }, DEBOUNCE_MS);

    return () => {
      if (timerRef.current != null) clearTimeout(timerRef.current);
    };
  }, [key, values, enabled]);
}

/**
 * <c>useDraftRecovery(key)</c> — al montar, lee el draft existente y
 * lo expone al caller para que decida si mostrar modal "Recuperar
 * borrador". El estado <c>recovered</c> se memoriza tras la primera
 * lectura; subsecuentes renders no re-leen.
 *
 * <para>Devuelve también <c>discard()</c> para limpiar el draft sin
 * recuperarlo (cuando el usuario click "Descartar" en el modal).</para>
 */
export function useDraftRecovery<T>(
  key: string | null | undefined,
): {
  draft: StoredDraft<T> | null;
  /** Marca el draft como "ya lo presenté al usuario". */
  acknowledge: () => void;
  /** Borra el draft del storage Y oculta el modal. */
  discard: () => void;
} {
  const [draft, setDraft] = useState<StoredDraft<T> | null>(() =>
    key != null ? readDraft<T>(key) : null,
  );

  return {
    draft,
    acknowledge: () => setDraft(null),
    discard: () => {
      if (key != null) clearDraft(key);
      setDraft(null);
    },
  };
}
