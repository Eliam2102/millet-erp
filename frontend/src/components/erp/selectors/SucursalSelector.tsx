import { useSucursales } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;SucursalSelector/&gt;</c> — selector de sucursal. Doc 05
 * §11.3. Patrón eager (catálogo chico, búsqueda client-side).
 */
export interface SucursalSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function SucursalSelector({
  value,
  onChange,
  placeholder = 'Selecciona sucursal',
  disabled,
  className,
}: SucursalSelectorProps) {
  const query = useSucursales();
  const items = query.data?.items ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(s) => `${s.clave} ${s.nombre}`}
      renderTrigger={(s) => `${s.clave} · ${s.nombre}`}
      renderItem={(s) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{s.clave}</span>
          <p className="truncate text-sm">{s.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar sucursal…"
      emptyListText="No hay sucursales en el catálogo."
      ariaLabel="Seleccionar sucursal"
      disabled={disabled}
      className={className}
    />
  );
}
