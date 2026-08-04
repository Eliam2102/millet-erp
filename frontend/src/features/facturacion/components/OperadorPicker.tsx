import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useOperadoresAdmin } from '@/features/facturacion/api/cartaPorteCatalogos';
import type { OperadorListItem } from '@/features/facturacion/api/types';

/**
 * <c>&lt;OperadorPicker/&gt;</c> — combobox eager de los operadores
 * (choferes) ACTIVOS del catálogo de Carta Porte — cierra
 * PLATFORM-TODO(&lt;OperadorPicker&gt;). Sustituye la captura del GUID a
 * mano en Nueva Carta Porte / Siguiente tramo. El alta/edición vive en
 * <c>/admin/carta-porte-catalogos</c>.
 */
export interface OperadorPickerProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /** Etiqueta del trigger mientras el catálogo carga (cold value). */
  initialLabel?: string | null;
  disabled?: boolean;
  className?: string;
}

export function OperadorPicker({
  value,
  onChange,
  initialLabel,
  disabled,
  className,
}: OperadorPickerProps) {
  const query = useOperadoresAdmin(false);
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox<OperadorListItem>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(o) => `${o.rfc} ${o.nombre}`}
      renderItem={(o) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{o.rfc}</span>
            <span className="text-xs text-muted-foreground">
              Lic. {o.numLicencia}
            </span>
          </div>
          <p className="truncate text-sm">{o.nombre}</p>
        </div>
      )}
      renderTrigger={(o) => `${o.nombre} · ${o.rfc}`}
      initialLabel={initialLabel}
      placeholder="Selecciona el operador…"
      searchPlaceholder="Buscar por RFC o nombre…"
      emptyListText="No hay operadores activos. Da de alta en Configuración → Vehículos y operadores."
      ariaLabel="Seleccionar operador"
      disabled={disabled}
      className={className}
    />
  );
}
