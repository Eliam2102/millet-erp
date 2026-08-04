import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useEstadoCuentaAnticipos } from '@/features/facturacion/api/useAnticipos';
import type { AnticipoEstadoCuenta } from '@/features/facturacion/api/types';

/**
 * <c>&lt;AnticipoPicker/&gt;</c> — combobox eager de los anticipos
 * ABIERTOS con saldo del cliente (FAC-UX-PR4, cierra
 * PLATFORM-TODO(&lt;AnticipoPicker&gt;)). Sustituye la captura del GUID
 * a mano: lista folio + saldo desde el estado de cuenta
 * (<c>GET /anticipos/control/{clienteId}</c>).
 *
 * <para><c>onChange</c> entrega el item completo para que el caller
 * proponga el importe a amortizar (default: el saldo).</para>
 */
export interface AnticipoPickerProps {
  clienteId: string | null | undefined;
  value: string | null | undefined;
  onChange: (item: AnticipoEstadoCuenta | null) => void;
  disabled?: boolean;
  className?: string;
}

type AnticipoConId = AnticipoEstadoCuenta & { id: string };

export function AnticipoPicker({
  clienteId,
  value,
  onChange,
  disabled,
  className,
}: AnticipoPickerProps) {
  const query = useEstadoCuentaAnticipos(clienteId);

  const abiertos = useMemo<AnticipoConId[]>(
    () =>
      (query.data?.anticipos ?? [])
        // 13-J: un anticipo cuyo CFDI no está timbrado no es amortizable —
        // la relación 07 exige el UUID (el backend lo rechaza con
        // ANTICIPO_CFDI_NO_TIMBRADO; aquí ni se ofrece).
        .filter(
          (a) => a.estado === 'Abierto' && a.saldo > 0 && a.estadoCfdi === 'Timbrado',
        )
        .map((a) => ({ ...a, id: a.anticipoId })),
    [query.data],
  );

  if (clienteId == null) {
    return (
      <p className="text-xs text-muted-foreground">
        Selecciona primero el cliente para listar sus anticipos abiertos.
      </p>
    );
  }

  return (
    <CatalogoEagerCombobox<AnticipoConId>
      items={abiertos}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={(id) =>
        onChange(id == null ? null : abiertos.find((a) => a.id === id) ?? null)
      }
      itemToLabel={(a) => `${a.folio} ${a.tipoAnticipo}`}
      renderItem={(a) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{a.folio}</span>
            <span className="text-xs text-muted-foreground">{a.tipoAnticipo}</span>
          </div>
          <p className="text-sm tabular-nums">
            Saldo {a.saldo.toFixed(2)}
            {a.pedidoOrigenRef ? ` · pedido ${a.pedidoOrigenRef}` : ''}
          </p>
        </div>
      )}
      renderTrigger={(a) => `${a.folio} · saldo ${a.saldo.toFixed(2)}`}
      placeholder="Selecciona un anticipo abierto…"
      searchPlaceholder="Buscar por folio…"
      emptyListText="El cliente no tiene anticipos abiertos con saldo."
      ariaLabel="Seleccionar anticipo a amortizar"
      disabled={disabled}
      className={className}
    />
  );
}
