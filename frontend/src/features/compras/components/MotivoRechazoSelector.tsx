import { useMemo } from 'react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useMotivosRechazo } from '@/features/compras/api';
import {
  MotivoRechazoAplicaA,
  aplicaABitmaskIncluye,
  type MotivoRechazoResponse,
} from '@/features/compras/api/types';
import { cn } from '@/lib/utils';
import { mensajeErrorCatalogo } from '@/components/erp/selectors/catalogo-error';

/**
 * <c>&lt;MotivoRechazoSelector/&gt;</c> — selector de motivo para
 * Rechazar / Eliminar / Cancelar una RQ (doc 05 §13.3 + §11.3).
 *
 * <para>Filtra el catálogo por bitmask <c>aplicaA</c> según el flujo
 * actual: el modal de Rechazar muestra solo motivos con
 * <c>Rechazo</c>; el de Eliminar con <c>Eliminacion</c>; el de
 * Cancelar con <c>Cancelacion</c>. Un motivo puede aplicar a varios
 * (ej. <c>OTRO</c> aplica a los 3).</para>
 *
 * <para>Cuando el motivo seleccionado tiene <c>permiteTextoLibre=true</c>,
 * el caller debe mostrar el textarea de detalle como REQUERIDO. Este
 * componente solo expone el motivo seleccionado vía <c>onChange</c>;
 * el caller hace el lookup contra <c>options</c> y decide.</para>
 */
export interface MotivoRechazoSelectorProps {
  /** Bitmask del flujo: <c>Rechazo</c>, <c>Eliminacion</c>, o <c>Cancelacion</c>. */
  aplicaA: MotivoRechazoAplicaA;
  /** Id del motivo seleccionado. */
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Callback adicional con el motivo completo seleccionado, para que
   * el caller maneje la lógica condicional de
   * <c>permiteTextoLibre</c> (mostrar textarea, hacer required, etc.).
   */
  onMotivoChange?: (motivo: MotivoRechazoResponse | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
}

const SENTINEL_NONE = '__none__';

export function MotivoRechazoSelector({
  aplicaA,
  value,
  onChange,
  onMotivoChange,
  placeholder = 'Selecciona motivo',
  disabled,
  className,
  id,
}: MotivoRechazoSelectorProps) {
  const motivosQuery = useMotivosRechazo();

  // Filtra los motivos cuyo bitmask incluye el flag del flujo actual.
  const opciones = useMemo(() => {
    const items = motivosQuery.data ?? [];
    return items.filter((m) => aplicaABitmaskIncluye(m.aplicaA, aplicaA));
  }, [motivosQuery.data, aplicaA]);

  function handleChange(next: string) {
    if (next === SENTINEL_NONE) {
      onChange(null);
      onMotivoChange?.(null);
      return;
    }
    const motivo = opciones.find((m) => m.id === next) ?? null;
    onChange(next);
    onMotivoChange?.(motivo);
  }

  const triggerValue = value ?? SENTINEL_NONE;

  return (
    <>
      <Select
        value={triggerValue}
        onValueChange={handleChange}
        disabled={disabled}
      >
        <SelectTrigger
          id={id}
          aria-label="Seleccionar motivo"
          className={cn('w-full', className)}
        >
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
        <SelectContent>
          {motivosQuery.isLoading ? (
            <div className="px-3 py-2 text-sm text-muted-foreground">
              Cargando motivos…
            </div>
          ) : opciones.length === 0 ? (
            <div className="px-3 py-2 text-sm text-muted-foreground">
              No hay motivos disponibles para este flujo.
            </div>
          ) : (
            opciones.map((m) => (
              <SelectItem key={m.id} value={m.id}>
                <div className="flex items-center gap-2">
                  <span className="font-mono text-xs text-muted-foreground">
                    {m.clave}
                  </span>
                  <span>{m.descripcion}</span>
                  {m.permiteTextoLibre && (
                    <span
                      aria-label="Requiere detalle adicional"
                      title="Requiere detalle adicional"
                      className="text-xs text-amber-600"
                    >
                      *
                    </span>
                  )}
                </div>
              </SelectItem>
            ))
          )}
        </SelectContent>
      </Select>
      {motivosQuery.isError && (
        <p role="alert" className="text-xs text-destructive">
          {mensajeErrorCatalogo(motivosQuery.error)}
        </p>
      )}
    </>
  );
}

/**
 * Re-export de la flag bitmask para que el caller pueda construir
 * combinaciones (<c>Rechazo | Cancelacion</c>) sin importar dos veces.
 */
export { MotivoRechazoAplicaA };
