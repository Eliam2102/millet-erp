import { usePuestos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/features/compras/api/types';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;PuestoSelector/&gt;</c> — selector de puesto organizacional
 * (ADM-FE-PR1, doc 10-catalogo-puestos-empleados). Patrón eager
 * (catálogo chico, búsqueda client-side), gemelo de
 * <c>SucursalSelector</c>. Solo lista puestos activos — el universo
 * elegible para asignación y políticas de viáticos.
 */
export interface PuestoSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function PuestoSelector({
  value,
  onChange,
  placeholder = 'Selecciona puesto',
  disabled,
  className,
}: PuestoSelectorProps) {
  const query = usePuestos();
  const items = (query.data?.items ?? []).filter(
    (p) => p.estatus === EstatusCatalogo.Activo || p.id === value,
  );

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(p) => `${p.clave} ${p.nombre}`}
      renderTrigger={(p) => `${p.clave} · ${p.nombre}`}
      renderItem={(p) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{p.clave}</span>
          <p className="truncate text-sm">{p.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar puesto…"
      emptyListText="No hay puestos en el catálogo."
      ariaLabel="Seleccionar puesto"
      disabled={disabled}
      className={className}
    />
  );
}
