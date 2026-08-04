import { useUbicaciones } from '@/features/almacen/api/useUbicaciones';
import { rutaUbicacion } from '@/features/almacen/lib/ubicacion-label';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;UbicacionSelector/&gt;</c> — selector plano de ubicación N4 (ADR-0047
 * PR C). Molde <c>AlmacenSelector</c> (eager sobre <c>CatalogoEagerCombobox</c>),
 * poblado por <c>useUbicaciones</c> (<c>GET /api/v1/almacen/ubicaciones</c>).
 *
 * <para>Hoy toda ubicación es la "ÚNICA" de su sub-almacén (una por sub-almacén,
 * misma clave), así que la etiqueta muestra la ruta del padre —
 * <c>"{almacenClave} › {subAlmacenClave} · {clave}"</c> — para distinguirlas.</para>
 */
export interface UbicacionSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /** Etiqueta inicial (cold value) cuando el id no está en la lista cargada. */
  initialLabel?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function UbicacionSelector({
  value,
  onChange,
  initialLabel,
  placeholder = 'Selecciona ubicación',
  disabled,
  className,
}: UbicacionSelectorProps) {
  const query = useUbicaciones({ limit: 500 });
  const items = query.data?.items ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      initialLabel={initialLabel}
      itemToLabel={(u) =>
        `${u.almacenClave} ${u.subAlmacenClave} ${u.clave} ${u.nombre} ${u.subAlmacenNombre}`
      }
      renderTrigger={rutaUbicacion}
      renderItem={(u) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{rutaUbicacion(u)}</span>
          <p className="truncate text-sm text-muted-foreground">
            {u.subAlmacenNombre}
          </p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar ubicación…"
      emptyListText="No hay ubicaciones en el catálogo."
      ariaLabel="Seleccionar ubicación"
      disabled={disabled}
      className={className}
    />
  );
}
