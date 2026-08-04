import { useIncoterms } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;CceIncotermSelector/&gt;</c> — selector de INCOTERM para el
 * Comercio Exterior (CCE). Reusa el catálogo local seedeado
 * (<c>useIncoterms</c>, mismo que OC/CxP/Recepción) pero su value es el
 * CÓDIGO SAT c_INCOTERM (FOB, CIF…), que es lo que exige el nodo del CCE —
 * a diferencia de <c>&lt;IncotermSelector/&gt;</c>, cuyo value es el id
 * GUID del catálogo.
 */
export interface CceIncotermSelectorProps {
  /** Código SAT c_INCOTERM seleccionado (p.ej. 'FOB') o null. */
  value: string | null | undefined;
  onChange: (codigo: string | null) => void;
  disabled?: boolean;
  className?: string;
}

export function CceIncotermSelector({
  value,
  onChange,
  disabled,
  className,
}: CceIncotermSelectorProps) {
  const query = useIncoterms();
  // Reclavamos el catálogo por CÓDIGO: CatalogoEagerCombobox opera sobre
  // `id`, y el CCE necesita el código SAT (FOB), no el GUID del catálogo.
  const items = (query.data ?? []).map((i) => ({ ...i, id: i.codigo }));

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
      placeholder="Selecciona Incoterm"
      searchPlaceholder="Buscar Incoterm…"
      emptyListText="No hay Incoterms en el catálogo."
      ariaLabel="Seleccionar Incoterm (CCE)"
      disabled={disabled}
      className={className}
    />
  );
}
