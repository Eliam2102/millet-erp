import { useMemo, useState } from 'react';
import { TriangleAlert, ChevronDown, ChevronRight } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ConflictResolutionDialog/&gt;</c> — diálogo que se abre cuando
 * una mutation devuelve <c>409 CONCURRENCY_CONFLICT</c>. Doc 05 §8.4
 * (modo preserve) y §8.5 (modo simple); F9 Rev. 3 confirmó
 * "Reaplicar como botón primario default".
 *
 * <para><b>Dos modos</b>:</para>
 * <list type="number">
 *   <item><b>Modo simple</b> (no se pasa <c>form</c>): "esta entidad fue
 *   actualizada por otro usuario. Refresca para ver el estado actual."
 *   Botones: "Refrescar y revisar" (default) | "Cancelar". Lo usan las
 *   acciones sin form local: aprobar, rechazar, transmitir, eliminar,
 *   cancelar.</item>
 *   <item><b>Modo preserve</b> (se pasan <c>form</c>, <c>localValues</c>,
 *   <c>baselineValues</c>, <c>latestRemote</c>): el dialog calcula el
 *   diff filtrado (cambios remotos que solapan con los locales),
 *   muestra "Tus cambios pendientes" + "Cambios remotos relevantes".
 *   Botones: <b>"Reaplicar mis cambios"</b> (primario, default focus) |
 *   "Solo refrescar (descartar mis cambios)" | "Cancelar". Lo usan las
 *   mutations desde formularios: P4 nueva, <c>LineaInlineForm</c>,
 *   edición de notas, PATCH cabecera.</item>
 * </list>
 *
 * <para><b>Captura de form state</b>: el caller captura
 * <c>localValues = form.getValues()</c> ANTES de invalidar la query
 * (doc 05 §8.4 paso "captura el form state local en memoria antes de
 * cualquier refresh"). El dialog NO captura por sí solo — eso evita el
 * antipatrón de <c>useEffect + setState</c> en el render del modal y
 * desacopla el momento del snapshot.</para>
 *
 * <para>UF0-PR2 entrega el componente con su lógica completa; UF3-PR2
 * (aislado por riesgo según §14.6 del 05) wirea las mutations a este
 * dialog.</para>
 */

/**
 * Interfaz mínima compatible con <c>UseFormReturn&lt;TFieldValues&gt;</c>
 * de <c>react-hook-form</c>. Define lo que el dialog necesita sin
 * acoplarse a un schema concreto. Solo se usa para el comportamiento
 * default de los botones (reset al merge / al remote).
 */
export interface ConflictDialogForm {
  reset: (values: Record<string, unknown>) => void;
}

export interface ConflictResolutionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;

  /**
   * Callback al elegir "Refrescar" (modo simple) o "Solo refrescar
   * (descartar mis cambios)" (modo preserve). El caller invalida la
   * query / refetchea. El dialog se cierra automáticamente después.
   */
  onRefrescar: () => void;

  // ----- Modo preserve (los 4 deben venir juntos para activarlo) -----
  /** Form react-hook-form (interfaz mínima: solo <c>reset</c>). */
  form?: ConflictDialogForm;
  /**
   * Valores locales del form al momento del conflicto. El caller los
   * captura con <c>form.getValues()</c> ANTES de invalidar la query.
   */
  localValues?: Record<string, unknown>;
  /**
   * Valores baseline (lo que el form tenía al cargar la entidad).
   * Típicamente <c>form.formState.defaultValues</c>.
   */
  baselineValues?: Record<string, unknown>;
  /**
   * Snapshot remoto recién cargado (típicamente
   * <c>queryClient.getQueryData(...)</c> tras invalidar y refetch).
   */
  latestRemote?: Record<string, unknown>;
  /**
   * Override del comportamiento de "Reaplicar mis cambios". Si se
   * omite, el dialog hace por default
   * <c>form.reset({ ...latestRemote, ...cambiosLocales })</c>.
   */
  onReaplicar?: () => void;
  /**
   * Subset de campos a considerar en el diff. Si se omite, usa el
   * unión de claves de <c>local</c>, <c>latestRemote</c> y
   * <c>baseline</c>. Útil para excluir campos de auditoría
   * (<c>version</c>, <c>updatedAt</c>) del diff visible.
   */
  campos?: string[];
  /** Etiquetas humanas por campo (ej. <c>{ cantidad: 'Cantidad' }</c>). */
  fieldLabels?: Record<string, string>;

  /** TraceId para mostrar al usuario (diagnóstico de soporte). */
  traceId?: string;
}

interface DiffEntry {
  campo: string;
  /** Etiqueta visible (de <c>fieldLabels</c> si existe). */
  label: string;
  /** Valor en la versión "base" (lo que el usuario tenía al cargar). */
  baseline: unknown;
  /** Valor que el usuario tipeó. */
  local: unknown;
  /** Valor que el otro usuario / servidor escribió. */
  remoto: unknown;
}

export function ConflictResolutionDialog({
  open,
  onOpenChange,
  onRefrescar,
  form,
  localValues,
  baselineValues,
  latestRemote,
  onReaplicar,
  campos,
  fieldLabels,
  traceId,
}: ConflictResolutionDialogProps) {
  const modoPreserve =
    localValues != null && baselineValues != null && latestRemote != null;

  const { cambiosLocales, cambiosRemotos, solapes } = useMemo(
    () =>
      modoPreserve
        ? calcularDiff({
            local: localValues,
            baseline: baselineValues,
            remote: latestRemote,
            campos,
            fieldLabels,
          })
        : { cambiosLocales: [], cambiosRemotos: [], solapes: [] },
    [
      modoPreserve,
      localValues,
      baselineValues,
      latestRemote,
      campos,
      fieldLabels,
    ],
  );

  const [verTodosRemotos, setVerTodosRemotos] = useState(false);

  function handleReaplicar() {
    if (onReaplicar != null) {
      onReaplicar();
    } else if (modoPreserve && form != null) {
      const cambiosLocalesObj = Object.fromEntries(
        cambiosLocales.map((c) => [c.campo, c.local]),
      );
      form.reset({ ...latestRemote, ...cambiosLocalesObj });
    }
    onOpenChange(false);
  }

  function handleSoloRefrescar() {
    if (modoPreserve && form != null) {
      // Descarta cambios locales realineando con el servidor.
      form.reset({ ...latestRemote });
    }
    onRefrescar();
    onOpenChange(false);
  }

  function handleCancelar() {
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <TriangleAlert className="h-5 w-5 text-amber-500" />
            {modoPreserve
              ? 'Otro usuario modificó esta requisición mientras la editabas'
              : 'Esta requisición fue actualizada por otro usuario'}
          </DialogTitle>
          <DialogDescription>
            {modoPreserve
              ? 'Tu trabajo NO se perdió — abajo puedes revisar qué cambió y decidir si lo reaplicas.'
              : 'Refresca para ver el estado actual antes de reintentar.'}
          </DialogDescription>
        </DialogHeader>

        {modoPreserve && (
          <div className="space-y-4 text-sm">
            {/* Sección 1: Cambios remotos relevantes (diff filtrado) */}
            <section>
              <h4 className="mb-2 font-medium">Cambios remotos relevantes</h4>
              {solapes.length > 0 ? (
                <DiffTable
                  entries={solapes}
                  columnaIzq="Tu valor"
                  columnaDer="Valor remoto"
                  pickIzq={(e) => e.local}
                  pickDer={(e) => e.remoto}
                />
              ) : (
                <div className="rounded-md border bg-muted/40 p-3">
                  <p className="text-muted-foreground">
                    Tus cambios no se solapan con los del otro usuario.
                  </p>
                  {cambiosRemotos.length > 0 && (
                    <button
                      type="button"
                      className="mt-2 inline-flex items-center gap-1 text-xs underline-offset-2 hover:underline"
                      onClick={() => setVerTodosRemotos((v) => !v)}
                      aria-expanded={verTodosRemotos}
                    >
                      {verTodosRemotos ? (
                        <ChevronDown className="h-3 w-3" />
                      ) : (
                        <ChevronRight className="h-3 w-3" />
                      )}
                      El otro usuario también cambió{' '}
                      {cambiosRemotos.length === 1
                        ? '1 campo'
                        : `${cambiosRemotos.length} campos`}
                      . {verTodosRemotos ? 'Ocultar' : 'Ver todos'}
                    </button>
                  )}
                  {verTodosRemotos && (
                    <div className="mt-3">
                      <DiffTable
                        entries={cambiosRemotos}
                        columnaIzq="Antes"
                        columnaDer="Ahora"
                        pickIzq={(e) => e.baseline}
                        pickDer={(e) => e.remoto}
                      />
                    </div>
                  )}
                </div>
              )}
            </section>

            {/* Sección 2: Tus cambios pendientes */}
            <section>
              <h4 className="mb-2 font-medium">Tus cambios pendientes</h4>
              {cambiosLocales.length > 0 ? (
                <DiffTable
                  entries={cambiosLocales}
                  columnaIzq="Original"
                  columnaDer="Tu valor"
                  pickIzq={(e) => e.baseline}
                  pickDer={(e) => e.local}
                />
              ) : (
                <p className="text-muted-foreground">
                  No tenías cambios sin guardar.
                </p>
              )}
            </section>
          </div>
        )}

        {traceId != null && (
          <p className="font-mono text-xs text-muted-foreground">
            Código: {traceId}
          </p>
        )}

        <DialogFooter className="flex-col gap-2 sm:flex-row sm:justify-end">
          <Button
            type="button"
            variant="ghost"
            onClick={handleCancelar}
            className="sm:order-1"
          >
            Cancelar
          </Button>
          {modoPreserve ? (
            <>
              <Button
                type="button"
                variant="outline"
                onClick={handleSoloRefrescar}
                className="sm:order-2"
              >
                Solo refrescar (descartar mis cambios)
              </Button>
              <Button
                type="button"
                onClick={handleReaplicar}
                autoFocus
                className="sm:order-3"
              >
                Reaplicar mis cambios
              </Button>
            </>
          ) : (
            <Button
              type="button"
              onClick={handleSoloRefrescar}
              autoFocus
              className="sm:order-2"
            >
              Refrescar y revisar
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Calcula los tres conjuntos del diff en una sola pasada:
 *
 * - <c>cambiosLocales</c>: campos cuyo valor local difiere del baseline
 *   (lo que el usuario tipeó).
 * - <c>cambiosRemotos</c>: campos cuyo valor remoto difiere del baseline
 *   (lo que otro usuario / proceso escribió).
 * - <c>solapes</c>: intersección — los campos que ambos tocaron y
 *   además quedaron en valores distintos. Estos son los que se muestran
 *   en la sección 1 del dialog (diff filtrado).
 */
function calcularDiff({
  local,
  baseline,
  remote,
  campos,
  fieldLabels,
}: {
  local: Record<string, unknown>;
  baseline: Record<string, unknown>;
  remote: Record<string, unknown>;
  campos?: string[];
  fieldLabels?: Record<string, string>;
}): {
  cambiosLocales: DiffEntry[];
  cambiosRemotos: DiffEntry[];
  solapes: DiffEntry[];
} {
  const claves =
    campos ??
    Array.from(
      new Set([
        ...Object.keys(local),
        ...Object.keys(remote),
        ...Object.keys(baseline),
      ]),
    );

  const labelOf = (campo: string): string =>
    fieldLabels?.[campo] ?? humanizar(campo);

  const cambiosLocales: DiffEntry[] = [];
  const cambiosRemotos: DiffEntry[] = [];
  const solapes: DiffEntry[] = [];

  for (const campo of claves) {
    const entry: DiffEntry = {
      campo,
      label: labelOf(campo),
      baseline: baseline[campo],
      local: local[campo],
      remoto: remote[campo],
    };
    const cambioLocal = !equalDeep(entry.baseline, entry.local);
    const cambioRemoto = !equalDeep(entry.baseline, entry.remoto);
    if (cambioLocal) cambiosLocales.push(entry);
    if (cambioRemoto) cambiosRemotos.push(entry);
    if (cambioLocal && cambioRemoto && !equalDeep(entry.local, entry.remoto)) {
      solapes.push(entry);
    }
  }

  return { cambiosLocales, cambiosRemotos, solapes };
}

/**
 * Igualdad estructural simple — suficiente para los valores típicos de
 * un form (string/number/boolean/null/undefined/Date/arrays planos).
 * Si en el futuro hay valores más complejos, considerar
 * <c>fast-deep-equal</c>.
 */
function equalDeep(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (a == null || b == null) return false;
  if (typeof a !== typeof b) return false;
  if (a instanceof Date && b instanceof Date) {
    return a.getTime() === b.getTime();
  }
  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) return false;
    return a.every((v, i) => equalDeep(v, b[i]));
  }
  if (typeof a === 'object' && typeof b === 'object') {
    const ak = Object.keys(a as object);
    const bk = Object.keys(b as object);
    if (ak.length !== bk.length) return false;
    return ak.every((k) =>
      equalDeep(
        (a as Record<string, unknown>)[k],
        (b as Record<string, unknown>)[k],
      ),
    );
  }
  return false;
}

/**
 * Convierte <c>requisitanteId</c> → <c>"Requisitante id"</c> como
 * fallback cuando no hay <c>fieldLabels</c>. Para etiquetas pulidas, el
 * caller pasa el mapa.
 */
function humanizar(camelCase: string): string {
  return camelCase
    .replace(/([A-Z])/g, ' $1')
    .replace(/^./, (c) => c.toUpperCase())
    .trim();
}

interface DiffTableProps {
  entries: DiffEntry[];
  columnaIzq: string;
  columnaDer: string;
  pickIzq: (entry: DiffEntry) => unknown;
  pickDer: (entry: DiffEntry) => unknown;
}

function DiffTable({
  entries,
  columnaIzq,
  columnaDer,
  pickIzq,
  pickDer,
}: DiffTableProps) {
  return (
    <div className="overflow-hidden rounded-md border">
      <table className="w-full text-xs">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left font-medium">Campo</th>
            <th className="px-3 py-2 text-left font-medium">{columnaIzq}</th>
            <th className="px-3 py-2 text-left font-medium">{columnaDer}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e, i) => (
            <tr
              key={e.campo}
              className={cn('border-t', i % 2 === 1 && 'bg-muted/20')}
            >
              <td className="px-3 py-2 font-medium">{e.label}</td>
              <td className="px-3 py-2 font-mono text-muted-foreground">
                {formatearValor(pickIzq(e))}
              </td>
              <td className="px-3 py-2 font-mono">
                {formatearValor(pickDer(e))}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Formatea un valor del diff para mostrar al usuario. Strings/numbers
 * se muestran tal cual; <c>null</c>/<c>undefined</c> como "—"; objetos
 * y arrays con <c>JSON.stringify</c>; Dates con <c>toISOString</c>.
 */
function formatearValor(v: unknown): string {
  if (v == null) return '—';
  if (typeof v === 'string') return v.length === 0 ? '—' : v;
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (v instanceof Date) return v.toISOString();
  try {
    return JSON.stringify(v);
  } catch {
    return String(v);
  }
}
