import { toast } from 'sonner';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import {
  MODO_NIVEL2_LABEL,
  ModoNivel2,
} from '@/features/compras/api/types';
import {
  useActualizarModoNivel2,
  useComprasConfiguracion,
} from '@/features/compras/api/useComprasConfiguracion';

/**
 * <c>&lt;ModoNivel2Selector/&gt;</c> — define qué condiciones exigen nivel 2
 * de autorización (<c>ComprasSettings.ModoNivel2</c>). Guarda al elegir
 * una opción (PATCH parcial con solo <c>modoNivel2</c>).
 *
 * <para>Sin permiso de editar el control queda deshabilitado; la página
 * explica por qué.</para>
 */
export interface ModoNivel2SelectorProps {
  puedeEditar: boolean;
}

export function ModoNivel2Selector({ puedeEditar }: ModoNivel2SelectorProps) {
  const configQuery = useComprasConfiguracion();
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarModoNivel2();

  if (configQuery.isPending) {
    return (
      <div className="rounded-lg bg-surface-card px-4 py-4 text-sm text-ink-muted shadow-card-flat">
        Cargando configuración…
      </div>
    );
  }

  if (configQuery.isError) {
    return (
      <div role="alert" className="rounded-lg bg-danger-bg px-4 py-4 text-sm text-danger-fg">
        No fue posible cargar la configuración de Compras.
      </div>
    );
  }

  const modoActual = configQuery.data.modoNivel2;

  function onChange(valor: string) {
    actualizar.mutate(
      { modoNivel2: valor as ModoNivel2, idempotencyKey },
      {
        onSuccess: () => toast.success('Modo de nivel 2 guardado.'),
        onError: (error) =>
          toast.error(
            esApiError(error)
              ? error.problem.detail ?? error.problem.title
              : 'No fue posible guardar el modo de nivel 2.',
          ),
      },
    );
  }

  return (
    <section
      aria-labelledby="modo-nivel2-titulo"
      className="rounded-lg bg-surface-card px-4 py-4 shadow-card-flat space-y-2"
    >
      <h2 id="modo-nivel2-titulo" className="text-sm font-semibold text-ink-strong">
        Modo de nivel 2
      </h2>
      <div className="flex flex-col gap-1.5 sm:max-w-sm">
        <label htmlFor="modo-nivel2" className="text-xs font-medium text-ink-strong">
          Condición que exige nivel 2
        </label>
        <Select
          value={modoActual}
          onValueChange={onChange}
          disabled={!puedeEditar || actualizar.isPending}
        >
          <SelectTrigger id="modo-nivel2" aria-describedby="modo-nivel2-ayuda">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {Object.values(ModoNivel2).map((modo) => (
              <SelectItem key={modo} value={modo}>
                {MODO_NIVEL2_LABEL[modo]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p id="modo-nivel2-ayuda" className="text-xs text-ink-muted">
          Las requisiciones de naturaleza Crítico o Riesgo siempre exigen nivel 2. Este modo
          decide si además lo exigen el monto (umbral N2 del departamento) o la disponibilidad
          en sucursal.
        </p>
      </div>
    </section>
  );
}
