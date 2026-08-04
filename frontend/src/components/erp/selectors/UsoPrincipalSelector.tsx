import { useUsosPrincipales } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;UsoPrincipalSelector/&gt;</c> — selector de uso principal de la
 * OC (UF2-PR1: catálogo seedeado de usos no-producción). Molde exacto de
 * <c>&lt;CondicionesPagoSelector/&gt;</c>: <c>CatalogoEagerCombobox</c>
 * eager con búsqueda + scroll (cap <c>max-h-[300px]</c> del
 * <c>CommandList</c>). A diferencia de condiciones, <c>UsoPrincipalItem</c>
 * NO trae <c>diasCredito</c>, así que el item es clave + nombre a secas.
 * Cross-módulo: OC (cabecera del alta).
 */
export interface UsoPrincipalSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function UsoPrincipalSelector({
  value,
  onChange,
  placeholder = 'Selecciona uso principal',
  disabled,
  className,
}: UsoPrincipalSelectorProps) {
  const query = useUsosPrincipales();
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(u) => `${u.clave} ${u.nombre}`}
      renderTrigger={(u) => `${u.clave} · ${u.nombre}`}
      renderItem={(u) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{u.clave}</span>
          <p className="truncate text-sm">{u.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar uso principal…"
      emptyListText="No hay usos principales en el catálogo."
      ariaLabel="Seleccionar uso principal"
      disabled={disabled}
      className={className}
    />
  );
}
