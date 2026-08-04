import { useMemo } from 'react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCuentasBancarias } from '@/features/tesoreria/api/useTesoreria';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { mensajeErrorCatalogo } from '@/components/erp/selectors/catalogo-error';

export interface CuentaBancariaSelectorProps {
  value: string | null;
  onChange: (cuentaId: string | null) => void;
  /**
   * RN-3 en UI: si se indica, solo se listan cuentas de esa moneda (la
   * moneda del movimiento ES la de la cuenta — el backend rechaza el
   * cross-moneda de todas formas).
   */
  moneda?: string | null;
  disabled?: boolean;
}

/**
 * <c>CuentaBancariaSelector</c> (TES-FE-PR2, 05-frontend-diseno §5):
 * selector de cuenta propia con número enmascarado (el masking viene del
 * server salvo permiso ver-cuenta-completa) y filtro por moneda.
 */
export function CuentaBancariaSelector({
  value,
  onChange,
  moneda = null,
  disabled = false,
}: CuentaBancariaSelectorProps) {
  const query = useCuentasBancarias(true);

  const cuentas = useMemo(() => {
    const todas = query.data ?? [];
    return moneda ? todas.filter((c) => c.moneda === moneda) : todas;
  }, [query.data, moneda]);

  return (
    <>
      <Select
        value={value ?? ''}
        onValueChange={(v) => onChange(v === '' ? null : v)}
        disabled={disabled || query.isLoading}
      >
        <SelectTrigger aria-label="Cuenta bancaria" className="w-full">
          <SelectValue
            placeholder={
              query.isLoading
                ? 'Cargando cuentas…'
                : cuentas.length === 0
                  ? moneda
                    ? `Sin cuentas activas en ${moneda}`
                    : 'Sin cuentas activas'
                  : 'Selecciona una cuenta'
            }
          />
        </SelectTrigger>
        <SelectContent>
          {cuentas.map((c) => (
            <SelectItem key={c.id} value={c.id}>
              <span className="flex items-center gap-2">
                <span>{c.banco}</span>
                <span className="font-mono text-xs text-muted-foreground">
                  {c.numeroCuenta}
                </span>
                <span className="text-xs text-muted-foreground">
                  {formatoMonto(c.saldo, c.moneda)}
                </span>
              </span>
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {query.isError && (
        <p role="alert" className="text-xs text-destructive">
          {mensajeErrorCatalogo(query.error)}
        </p>
      )}
    </>
  );
}
