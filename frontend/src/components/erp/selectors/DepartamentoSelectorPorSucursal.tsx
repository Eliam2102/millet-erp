import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useDepartamentosDeSucursal } from '@/modules/administracion/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';

/**
 * <c>&lt;DepartamentoSelectorPorSucursal/&gt;</c> — selector de
 * departamento <b>filtrado por sucursal</b> (PR-A3 / PR-A1 backend).
 *
 * <para>Variante del <see cref="DepartamentoSelector"/> que en lugar
 * del catálogo global consume las asignaciones N:M de la sucursal
 * indicada. Sólo expone departamentos en estatus
 * <see cref="EstatusCatalogo.Activo"/> — los Inactivos NO son
 * seleccionables para nuevas requisiciones (PR-A2 backend rechazaría
 * 422 si se cuelan).</para>
 *
 * <list type="bullet">
 *   <item><c>sucursalId === null</c> → disabled con placeholder de
 *         hint, sin disparar request.</item>
 *   <item>Sucursal sin departamentos activos asignados → lista vacía
 *         con copy explicativo.</item>
 * </list>
 */
export interface DepartamentoSelectorPorSucursalProps {
  /** Sucursal cuyas asignaciones definen el universo de deptos. */
  sucursalId: string | null;
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

interface DepartamentoOption {
  id: string;
  clave: string;
  nombre: string;
}

export function DepartamentoSelectorPorSucursal({
  sucursalId,
  value,
  onChange,
  placeholder = 'Selecciona departamento',
  disabled,
  className,
}: DepartamentoSelectorPorSucursalProps) {
  const query = useDepartamentosDeSucursal(sucursalId);

  const items = useMemo<DepartamentoOption[]>(() => {
    const asignaciones = query.data?.items ?? [];
    return asignaciones
      .filter((a) => a.estatus === EstatusCatalogo.Activo)
      .map((a) => ({
        id: a.departamentoId,
        clave: a.departamentoClave,
        nombre: a.departamentoNombre,
      }));
  }, [query.data]);

  const noSucursalSeleccionada = sucursalId == null;

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading && !noSucursalSeleccionada}
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
      placeholder={
        noSucursalSeleccionada
          ? 'Selecciona primero una sucursal'
          : placeholder
      }
      searchPlaceholder="Buscar departamento…"
      emptyListText={
        noSucursalSeleccionada
          ? 'Selecciona primero una sucursal.'
          : 'Esta sucursal no tiene departamentos activos asignados. Contacta al administrador.'
      }
      ariaLabel="Seleccionar departamento de la sucursal"
      disabled={disabled || noSucursalSeleccionada}
      className={className}
    />
  );
}
