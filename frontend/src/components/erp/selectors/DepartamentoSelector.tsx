import { useDepartamentos } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;DepartamentoSelector/&gt;</c> — selector de departamento
 * para form fields. Doc 05 §11.3. Catálogo chico, búsqueda
 * client-side por clave/nombre.
 */
export interface DepartamentoSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function DepartamentoSelector({
  value,
  onChange,
  placeholder = 'Selecciona departamento',
  disabled,
  className,
}: DepartamentoSelectorProps) {
  const query = useDepartamentos();
  const items = query.data?.items ?? [];

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
      emptyListText="No hay departamentos en el catálogo."
      ariaLabel="Seleccionar departamento"
      disabled={disabled}
      className={className}
    />
  );
}
