import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useVehiculosAdmin } from '@/features/facturacion/api/cartaPorteCatalogos';
import type { VehiculoListItem } from '@/features/facturacion/api/types';

/**
 * <c>&lt;VehiculoPicker/&gt;</c> — combobox eager de los vehículos ACTIVOS
 * del catálogo de Carta Porte — cierra PLATFORM-TODO(&lt;VehiculoPicker&gt;).
 * Sustituye la captura del GUID a mano en Nueva Carta Porte / Siguiente
 * tramo. El alta/edición vive en <c>/admin/carta-porte-catalogos</c>.
 */
export interface VehiculoPickerProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /** Etiqueta del trigger mientras el catálogo carga (cold value). */
  initialLabel?: string | null;
  disabled?: boolean;
  className?: string;
}

export function VehiculoPicker({
  value,
  onChange,
  initialLabel,
  disabled,
  className,
}: VehiculoPickerProps) {
  const query = useVehiculosAdmin(false);
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox<VehiculoListItem>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(v) => `${v.placa} ${v.configVehicular}`}
      renderItem={(v) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{v.placa}</span>
            <span className="text-xs text-muted-foreground">
              {v.configVehicular} · {v.anioModelo}
            </span>
          </div>
          <p className="text-xs tabular-nums text-muted-foreground">
            {v.pesoBrutoVehicular != null
              ? `Peso bruto ${v.pesoBrutoVehicular} t`
              : 'Sin peso bruto — no timbra CP 3.1'}
          </p>
        </div>
      )}
      renderTrigger={(v) =>
        `${v.placa} · ${v.configVehicular}${v.pesoBrutoVehicular != null ? ` · ${v.pesoBrutoVehicular} t` : ''}`
      }
      initialLabel={initialLabel}
      placeholder="Selecciona el vehículo…"
      searchPlaceholder="Buscar por placa o configuración…"
      emptyListText="No hay vehículos activos. Da de alta en Configuración → Vehículos y operadores."
      ariaLabel="Seleccionar vehículo"
      disabled={disabled}
      className={className}
    />
  );
}
