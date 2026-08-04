import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useFacturasCobrablesPpd } from '@/features/facturacion/api/useRepp';
import type { FacturaCobrablePpdItem } from '@/features/facturacion/api/types';

/**
 * <c>&lt;FacturaPpdPicker/&gt;</c> — combobox eager de las facturas PPD
 * timbradas con saldo por cobrar (cierra PLATFORM-TODO(&lt;FacturaPicker&gt;)).
 * Sustituye la captura del GUID a mano en <c>NuevoRepp</c>: lista folio +
 * cliente + saldo desde <c>GET /repp/facturas-cobrables</c> ([Decisión 13-K]:
 * saldo neto de NC y REPP previos vigentes).
 *
 * <para><c>receptorRfc</c> restringe al cliente de la primera factura elegida
 * (regla <c>REPP_MULTIPLES_CLIENTES</c>). <c>onChange</c> entrega el item
 * completo para que el caller proponga el importe pagado (default: el
 * saldo).</para>
 */
export interface FacturaPpdPickerProps {
  receptorRfc?: string | null;
  /** Facturas ya elegidas en otros renglones (se ocultan de la lista). */
  excluirIds?: ReadonlyArray<string>;
  value: string | null | undefined;
  onChange: (item: FacturaCobrablePpdItem | null) => void;
  disabled?: boolean;
  className?: string;
}

type FacturaConId = FacturaCobrablePpdItem & { id: string };

export function FacturaPpdPicker({
  receptorRfc,
  excluirIds,
  value,
  onChange,
  disabled,
  className,
}: FacturaPpdPickerProps) {
  const query = useFacturasCobrablesPpd(receptorRfc);

  const cobrables = useMemo<FacturaConId[]>(
    () =>
      (query.data ?? [])
        .filter((f) => f.facturaVentaId === value || !(excluirIds ?? []).includes(f.facturaVentaId))
        .map((f) => ({ ...f, id: f.facturaVentaId })),
    [query.data, excluirIds, value],
  );

  return (
    <CatalogoEagerCombobox<FacturaConId>
      items={cobrables}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={(id) =>
        onChange(id == null ? null : cobrables.find((f) => f.id === id) ?? null)
      }
      itemToLabel={(f) => `${f.folio} ${f.receptorNombre}`}
      renderItem={(f) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{f.folio}</span>
            <span className="truncate text-xs text-muted-foreground">{f.receptorNombre}</span>
          </div>
          <p className="text-sm tabular-nums">
            Saldo {f.saldo.toFixed(2)} {f.moneda} · parcialidad {f.numParcialidadSiguiente}
          </p>
        </div>
      )}
      renderTrigger={(f) => `${f.folio} · saldo ${f.saldo.toFixed(2)} ${f.moneda}`}
      placeholder="Selecciona una factura PPD con saldo…"
      searchPlaceholder="Buscar por folio o cliente…"
      emptyListText={
        receptorRfc
          ? 'El cliente no tiene facturas PPD con saldo por cobrar.'
          : 'No hay facturas PPD con saldo por cobrar.'
      }
      ariaLabel="Seleccionar factura PPD a cubrir"
      disabled={disabled}
      className={className}
    />
  );
}
