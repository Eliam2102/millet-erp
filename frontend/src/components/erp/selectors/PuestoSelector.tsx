import { usePuestos } from '@/features/catalogos/api';
import { usePuestosDeSucursal } from '@/modules/administracion/api';
import { EstatusCatalogo } from '@/features/compras/api/types';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;PuestoSelector/&gt;</c> — selector de puesto organizacional
 * (ADM-FE-PR1, doc 10-catalogo-puestos-empleados). Patrón eager
 * (catálogo chico, búsqueda client-side), gemelo de
 * <c>SucursalSelector</c>.
 * Si se especifica <c>sucursalId</c>, filtra exclusivamente los
 * puestos asignados y activos en esa sucursal.
 * Si se especifica <c>departamentoId</c>, filtra exclusivamente los
 * puestos pertenecientes a ese departamento.
 */
export interface PuestoSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  sucursalId?: string | null;
  departamentoId?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function PuestoSelector({
  value,
  onChange,
  sucursalId,
  departamentoId,
  placeholder = 'Selecciona puesto',
  disabled,
  className,
}: PuestoSelectorProps) {
  const globalQuery = usePuestos();
  const sucursalQuery = usePuestosDeSucursal(sucursalId ?? null);

  const query = sucursalId ? sucursalQuery : globalQuery;

  const items = sucursalId
    ? (sucursalQuery.data?.items ?? [])
        .filter((p) => {
          const esActivoOSeleccionado =
            p.estatus === EstatusCatalogo.Activo || p.puestoId === value;
          const matchDepto =
            !departamentoId ||
            p.departamentoId === departamentoId ||
            p.puestoId === value;
          return esActivoOSeleccionado && matchDepto;
        })
        // Un puesto puede estar en varios departamentos de la sucursal
        // (una fila por asignación): se muestra una sola vez.
        .filter(
          (p, i, filas) => filas.findIndex((f) => f.puestoId === p.puestoId) === i,
        )
        .map((p) => ({
          id: p.puestoId,
          clave: p.puestoClave,
          nombre: p.puestoNombre,
          estatus: p.estatus,
        }))
    : (globalQuery.data?.items ?? []).filter((p) => {
        const esActivoOSeleccionado =
          p.estatus === EstatusCatalogo.Activo || p.id === value;
        const matchDepto =
          !departamentoId ||
          p.departamentoId === departamentoId ||
          p.id === value;
        return esActivoOSeleccionado && matchDepto;
      });

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
      emptyListText={
        departamentoId
          ? 'No hay puestos asignados a este departamento.'
          : sucursalId
            ? 'No hay puestos asignados a esta sucursal.'
            : 'No hay puestos en el catálogo.'
      }
      ariaLabel="Seleccionar puesto"
      disabled={disabled}
      className={className}
    />
  );
}
