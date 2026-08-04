import { useEmpleados } from '@/features/catalogos/api';
import type { EmpleadoListItem } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/features/compras/api/types';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;EmpleadoSelector/&gt;</c> — selector de empleado del master
 * organizacional (ADM-FE-PR1, doc 10-catalogo-puestos-empleados).
 * Patrón eager, gemelo de <c>SucursalSelector</c>. Solo lista empleados
 * activos.
 *
 * <para><c>onSelectItem</c> entrega el item completo (con
 * <c>puestoId</c> y <c>jefeDirectoId</c>) para prellenar formularios —
 * p.ej. la solicitud de viáticos prellena tope de política y
 * autorizador N1. <c>excludeId</c> saca a un empleado de la lista
 * (jefe directo ≠ el propio empleado).</para>
 */
export interface EmpleadoSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /** Recibe el item completo al seleccionar (o null al limpiar). */
  onSelectItem?: (empleado: EmpleadoListItem | null) => void;
  /** Excluye un empleado de la lista (p.ej. self al elegir jefe). */
  excludeId?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function EmpleadoSelector({
  value,
  onChange,
  onSelectItem,
  excludeId,
  placeholder = 'Selecciona empleado',
  disabled,
  className,
}: EmpleadoSelectorProps) {
  const query = useEmpleados();
  const items = (query.data?.items ?? []).filter(
    (e) =>
      (e.estatus === EstatusCatalogo.Activo || e.id === value) &&
      e.id !== excludeId,
  );

  function handleChange(id: string | null) {
    onChange(id);
    onSelectItem?.(id ? (items.find((e) => e.id === id) ?? null) : null);
  }

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={handleChange}
      itemToLabel={(e) => `${e.clave} ${e.nombre} ${e.email ?? ''}`}
      renderTrigger={(e) => `${e.clave} · ${e.nombre}`}
      renderItem={(e) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{e.clave}</span>
          <p className="truncate text-sm">{e.nombre}</p>
          {e.email && (
            <p className="truncate text-xs text-muted-foreground">{e.email}</p>
          )}
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar empleado…"
      emptyListText="No hay empleados en el catálogo."
      ariaLabel="Seleccionar empleado"
      disabled={disabled}
      className={className}
    />
  );
}
