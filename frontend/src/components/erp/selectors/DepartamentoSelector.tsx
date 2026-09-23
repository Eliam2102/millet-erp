import { useDepartamentos } from '@/features/catalogos/api';
import { useDepartamentosDeSucursal } from '@/modules/administracion/api';
import { EstatusCatalogo } from '@/features/compras/api/types';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;DepartamentoSelector/&gt;</c> — selector de departamento
 * para form fields. Doc 05 §11.3. Catálogo chico, búsqueda
 * client-side por clave/nombre.
 * Si se especifica <c>sucursalId</c>, filtra exclusivamente los
 * departamentos asignados y activos en esa sucursal.
 */
export interface DepartamentoSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  sucursalId?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function DepartamentoSelector({
  value,
  onChange,
  sucursalId,
  placeholder = 'Selecciona departamento',
  disabled,
  className,
}: DepartamentoSelectorProps) {
  const globalQuery = useDepartamentos();
  const sucursalQuery = useDepartamentosDeSucursal(sucursalId ?? null);

  const query = sucursalId ? sucursalQuery : globalQuery;

  const items = sucursalId
    ? (sucursalQuery.data?.items ?? [])
        .filter((d) => d.estatus === EstatusCatalogo.Activo || d.departamentoId === value)
        .map((d) => ({
          id: d.departamentoId,
          clave: d.departamentoClave,
          nombre: d.departamentoNombre,
          estatus: d.estatus,
        }))
    : (globalQuery.data?.items ?? []).filter(
        (d) => d.estatus === EstatusCatalogo.Activo || d.id === value,
      );

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(d) => `${d.clave} ${d.nombre}`}
      renderTrigger={(d) => `${d.clave} · ${d.nombre}`}
      renderItem={(d) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{d.clave}</span>
          <p className="truncate text-sm">{d.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar departamento…"
      emptyListText={
        sucursalId
          ? 'No hay departamentos asignados a esta sucursal.'
          : 'No hay departamentos en el catálogo.'
      }
      ariaLabel="Seleccionar departamento"
      disabled={disabled}
      className={className}
    />
  );
}
