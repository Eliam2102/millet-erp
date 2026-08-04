import { useIncoterms } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;IncotermSelector/&gt;</c> — selector de Incoterm 2020 (F9-PR1).
 * Cross-módulo: usado por OC (informacion-importacion), CxP (captura
 * de fletes), Recepción.
 */
export interface IncotermSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function IncotermSelector({
  value,
  onChange,
  placeholder = 'Selecciona Incoterm',
  disabled,
  className,
}: IncotermSelectorProps) {
  const query = useIncoterms();
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(i) => `${i.codigo} ${i.nombre}`}
      renderTrigger={(i) => `${i.codigo} · ${i.nombre}`}
      renderItem={(i) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{i.codigo}</span>
          <p className="truncate text-sm">{i.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar Incoterm…"
      emptyListText="No hay Incoterms en el catálogo."
      ariaLabel="Seleccionar Incoterm"
      disabled={disabled}
      className={className}
    />
  );
}
