import { useMemo, useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useAsignarPermisos, usePermisos } from '@/modules/identidad/api';
import type { PermisoResponse } from '@/modules/identidad/api/types';
import { cn } from '@/lib/utils';

/**
 * Matriz de permisos del detalle del rol (tab "Permisos"). Render:
 *
 * <list>
 *   <item>Carga el catálogo con <c>usePermisos(agrupado=true)</c> y
 *   pinta un <c>Collapsible</c> por módulo. Cada permiso es un
 *   checkbox con label = <c>codigo</c> + descripción.</item>
 *   <item>Mantiene un <c>Set&lt;string&gt;</c> local con los ids
 *   seleccionados, inicializado desde <c>permisoIdsIniciales</c> y
 *   resincronizado cuando cambian.</item>
 *   <item>Botón "Guardar cambios" llama <c>PUT
 *   /roles/{id}/permisos</c> con el set completo (batch atómico, el
 *   backend reemplaza el conjunto). No autosave — siempre hay un click
 *   explícito.</item>
 * </list>
 *
 * <para><b>Decisión de UX</b>: batch atómico con set completo (no
 * diff). El backend lo soporta y elimina el riesgo de inconsistencia
 * si la UI envía un diff calculado sobre un snapshot stale. El
 * <c>Idempotency-Key</c> protege contra dobles clicks.</para>
 *
 * <para>Si <c>disabled=true</c> (rol del sistema o sin permiso
 * <c>identidad.roles.asignar-permisos</c>), los checkboxes quedan
 * deshabilitados y no hay botón Guardar.</para>
 */
export interface MatrizPermisosProps {
  rolId: string;
  permisoIdsIniciales: readonly string[];
  disabled?: boolean;
  /**
   * Razón por la cual la matriz está deshabilitada — se muestra como
   * banner sobre la matriz. Si <c>undefined</c>, no se muestra banner.
   */
  disabledHint?: string;
}

export function MatrizPermisos({
  rolId,
  permisoIdsIniciales,
  disabled,
  disabledHint,
}: MatrizPermisosProps) {
  const permisosQuery = usePermisos(true);
  const idempotencyKey = useFormIdempotencyKey();
  const asignar = useAsignarPermisos();

  // Si el caller cambia de rol (mismo componente, nuevo id) o el
  // backend devuelve un set distinto tras una invalidación, resincronizar.
  // Usamos el patrón "derive state during render" recomendado por React
  // (https://react.dev/reference/react/useState#storing-information-from-previous-renders):
  // mantenemos la key previa en estado y comparamos en render para
  // disparar el setState de reset. Evita useEffect/useRef y mantiene
  // la lista sincronizada con permisoIdsIniciales sin sobre-renders.
  const idsKey = useMemo(
    () => [...permisoIdsIniciales].sort().join('|'),
    [permisoIdsIniciales],
  );

  const [seleccionados, setSeleccionados] = useState<Set<string>>(
    () => new Set(permisoIdsIniciales),
  );
  const [prevSnapshot, setPrevSnapshot] = useState({ idsKey, rolId });
  if (prevSnapshot.idsKey !== idsKey || prevSnapshot.rolId !== rolId) {
    setPrevSnapshot({ idsKey, rolId });
    setSeleccionados(new Set(permisoIdsIniciales));
  }

  const inicialesSet = useMemo(
    () => new Set(permisoIdsIniciales),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [idsKey],
  );

  const dirty = useMemo(() => {
    if (seleccionados.size !== inicialesSet.size) return true;
    for (const id of seleccionados) {
      if (!inicialesSet.has(id)) return true;
    }
    return false;
  }, [seleccionados, inicialesSet]);

  function toggle(permisoId: string, checked: boolean | 'indeterminate') {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      if (checked === true) next.add(permisoId);
      else next.delete(permisoId);
      return next;
    });
  }

  function toggleModulo(items: readonly PermisoResponse[], checked: boolean) {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      for (const p of items) {
        if (checked) next.add(p.id);
        else next.delete(p.id);
      }
      return next;
    });
  }

  function handleGuardar() {
    asignar.mutate(
      {
        id: rolId,
        command: { permisoIds: [...seleccionados] },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Permisos actualizados');
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al guardar los permisos.');
        },
      },
    );
  }

  function handleDescartar() {
    setSeleccionados(new Set(permisoIdsIniciales));
  }

  if (permisosQuery.isLoading) {
    return (
      <div className="space-y-3">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-full' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  if (permisosQuery.isError) {
    const problem = esApiError(permisosQuery.error)
      ? permisosQuery.error.problem
      : undefined;
    return (
      <ErrorState problem={problem} onRetry={() => permisosQuery.refetch()} />
    );
  }

  const grupos = permisosQuery.data?.grupos ?? [];

  return (
    <section className="space-y-3" aria-label="Matriz de permisos">
      {disabled && disabledHint != null && (
        <div
          role="status"
          className="rounded-md border border-amber-300 bg-amber-50/50 px-3 py-2 text-xs text-amber-900"
        >
          {disabledHint}
        </div>
      )}

      <div className="space-y-2">
        {grupos.length === 0 ? (
          <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
            No hay permisos registrados en el catálogo.
          </div>
        ) : (
          grupos.map((g) => (
            <ModuloCollapsible
              key={g.modulo}
              modulo={g.modulo}
              items={g.items}
              seleccionados={seleccionados}
              onToggle={toggle}
              onToggleModulo={toggleModulo}
              disabled={disabled === true}
            />
          ))
        )}
      </div>

      {!disabled && (
        <div className="flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <p className="mr-auto text-xs text-muted-foreground">
            {seleccionados.size} permiso(s) seleccionado(s).
            {dirty && (
              <span className="ml-1 text-amber-700">
                Hay cambios sin guardar.
              </span>
            )}
          </p>
          <Button
            type="button"
            variant="ghost"
            onClick={handleDescartar}
            disabled={!dirty || asignar.isPending}
          >
            Descartar cambios
          </Button>
          <Button
            type="button"
            onClick={handleGuardar}
            disabled={!dirty || asignar.isPending}
          >
            {asignar.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      )}
    </section>
  );
}

interface ModuloCollapsibleProps {
  modulo: string;
  items: readonly PermisoResponse[];
  seleccionados: Set<string>;
  onToggle: (id: string, checked: boolean | 'indeterminate') => void;
  onToggleModulo: (items: readonly PermisoResponse[], checked: boolean) => void;
  disabled: boolean;
}

function ModuloCollapsible({
  modulo,
  items,
  seleccionados,
  onToggle,
  onToggleModulo,
  disabled,
}: ModuloCollapsibleProps) {
  const [open, setOpen] = useState(false);

  const totalModulo = items.length;
  const seleccionadosEnModulo = items.filter((p) =>
    seleccionados.has(p.id),
  ).length;
  const todosSeleccionados =
    totalModulo > 0 && seleccionadosEnModulo === totalModulo;
  const algunoSeleccionado = seleccionadosEnModulo > 0;

  return (
    <Collapsible
      open={open}
      onOpenChange={setOpen}
      className="rounded-md border bg-card"
    >
      <div className="flex items-center gap-2 px-3 py-2">
        <Checkbox
          checked={
            todosSeleccionados
              ? true
              : algunoSeleccionado
                ? 'indeterminate'
                : false
          }
          disabled={disabled}
          aria-label={`Seleccionar todos los permisos de ${modulo}`}
          onCheckedChange={(checked) =>
            onToggleModulo(items, checked === true)
          }
        />
        <CollapsibleTrigger
          className="flex flex-1 items-center justify-between gap-2 text-left text-sm font-medium hover:underline"
          aria-label={`Permisos de ${modulo}`}
        >
          <span className="capitalize">{modulo}</span>
          <span className="flex items-center gap-2 text-xs text-muted-foreground">
            {seleccionadosEnModulo}/{totalModulo}
            <ChevronRight
              className={cn(
                'h-4 w-4 transition-transform',
                open && 'rotate-90',
              )}
              aria-hidden="true"
            />
          </span>
        </CollapsibleTrigger>
      </div>

      <CollapsibleContent>
        <ul className="divide-y border-t" role="list">
          {items.map((p) => (
            <li key={p.id} className="flex items-start gap-2 px-3 py-2 pl-9">
              <Checkbox
                id={`permiso-${p.id}`}
                checked={seleccionados.has(p.id)}
                disabled={disabled}
                onCheckedChange={(c) => onToggle(p.id, c)}
                className="mt-0.5"
              />
              <label
                htmlFor={`permiso-${p.id}`}
                className="flex-1 cursor-pointer space-y-0.5"
              >
                <div className="font-mono text-xs">{p.codigo}</div>
                <div className="text-xs text-muted-foreground">
                  {p.descripcion}
                </div>
              </label>
            </li>
          ))}
        </ul>
      </CollapsibleContent>
    </Collapsible>
  );
}
