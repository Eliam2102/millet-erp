import { useMemo } from 'react';
import {
  useAlmacenes,
  useSubAlmacenes,
} from '@/features/almacen/api/useAlmacenes';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;SubAlmacenSelector/&gt;</c> — selector de sub-almacén N3 (ADR-0047 PR
 * C7.1). Eager sobre <c>CatalogoEagerCombobox</c>, poblado por
 * <c>useSubAlmacenes</c>. Como la clave del sub-almacén solo es única dentro de
 * su almacén (p.ej. "INS" se repite), la etiqueta antepone la clave del almacén
 * padre — <c>"{almacenClave} › {clave} · {nombre}"</c> — igual que el
 * <c>UbicacionSelector</c>. Se usa en el filtro y en el Sheet de ubicaciones.
 */
export interface SubAlmacenSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function SubAlmacenSelector({
  value,
  onChange,
  placeholder = 'Selecciona sub-almacén',
  disabled,
  className,
}: SubAlmacenSelectorProps) {
  const subQuery = useSubAlmacenes({ limit: 500 });
  const almacenesQuery = useAlmacenes({ limit: 500 });

  const almacenClavePorId = useMemo(() => {
    const map = new Map<string, string>();
    for (const a of almacenesQuery.data?.items ?? []) map.set(a.id, a.clave);
    return map;
  }, [almacenesQuery.data]);

  const items = subQuery.data?.items ?? [];

  const ruta = (s: { almacenId: string; clave: string }) =>
    `${almacenClavePorId.get(s.almacenId) ?? '—'} › ${s.clave}`;

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={subQuery.isLoading || almacenesQuery.isLoading}
      error={subQuery.error ?? almacenesQuery.error}
      value={value}
      onChange={onChange}
      itemToLabel={(s) =>
        `${almacenClavePorId.get(s.almacenId) ?? ''} ${s.clave} ${s.nombre}`
      }
      renderTrigger={(s) => `${ruta(s)} · ${s.nombre}`}
      renderItem={(s) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{ruta(s)}</span>
          <p className="truncate text-sm text-muted-foreground">{s.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar sub-almacén…"
      emptyListText="No hay sub-almacenes en el catálogo."
      ariaLabel="Seleccionar sub-almacén"
      disabled={disabled}
      className={className}
    />
  );
}
