import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useAutorizacionesActivo } from '@/features/facturacion/api/useActivos';
import {
  EstadoAutorizacionActivo,
  type AutorizacionActivoItem,
} from '@/features/facturacion/api/types';

/**
 * <c>&lt;AutorizacionPicker/&gt;</c> — combobox eager de las
 * autorizaciones de venta de activo fijo en estado <c>Autorizada</c>
 * (pendientes de usar), FAC-UX-PR4 — cierra
 * PLATFORM-TODO(&lt;AutorizacionPicker&gt;). Sustituye el pegado del
 * GUID que devuelve la pantalla "Autorización de activos".
 */
export interface AutorizacionPickerProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  className?: string;
}

export function AutorizacionPicker({
  value,
  onChange,
  disabled,
  className,
}: AutorizacionPickerProps) {
  const query = useAutorizacionesActivo(EstadoAutorizacionActivo.Autorizada);
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox<AutorizacionActivoItem>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(a) => `${a.activoRef} ${a.descripcion}`}
      renderItem={(a) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{a.activoRef}</span>
            <span className="text-xs text-muted-foreground">
              Autorizó {a.autorizadoPor}
            </span>
          </div>
          <p className="truncate text-sm">{a.descripcion}</p>
          <p className="text-xs tabular-nums text-muted-foreground">
            Precio venta {a.precioVenta.toFixed(2)}
          </p>
        </div>
      )}
      renderTrigger={(a) => `${a.activoRef} · ${a.descripcion}`}
      placeholder="Selecciona la autorización del Contador General…"
      searchPlaceholder="Buscar por activo o descripción…"
      emptyListText="No hay autorizaciones pendientes de usar."
      ariaLabel="Seleccionar autorización de venta de activo"
      disabled={disabled}
      className={className}
    />
  );
}
