import { useMemo, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCuentasBancarias } from '@/features/tesoreria/api/useTesoreria';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { admiteBeneficiaria, enmascararCuenta } from '../lib/cuentas-pago-sat';

const MANUAL = '__manual';

export interface CuentaBeneficiariaCampoProps {
  id: string;
  formaPago: string;
  moneda: string;
  cuenta: string | null;
  cuentaBancariaId: string | null;
  onCuenta: (cuenta: string | null) => void;
  onCuentaBancariaId: (id: string | null) => void;
  invalid?: boolean;
  disabled?: boolean;
}

/**
 * Cuenta beneficiaria del complemento de pago (86e3tfdve): se elige de las
 * cuentas de Millet de la moneda del pago (el número llega enmascarado y el
 * servidor resuelve la CLABE) o se captura a mano. Sin permiso para ver las
 * cuentas de Tesorería, o sin cuentas en esa moneda, solo se captura.
 */
export function CuentaBeneficiariaCampo({
  id,
  formaPago,
  moneda,
  cuenta,
  cuentaBancariaId,
  onCuenta,
  onCuentaBancariaId,
  invalid,
  disabled,
}: CuentaBeneficiariaCampoProps) {
  const admite = admiteBeneficiaria(formaPago);
  const puedeVerCuentas = useHasPermission(PermisosCanonicos.TesoreriaCuentasVer);
  const query = useCuentasBancarias(true, puedeVerCuentas && admite);
  const [manual, setManual] = useState(false);
  const cuentas = useMemo(
    () => (query.data ?? []).filter((c) => c.moneda === moneda.toUpperCase()),
    [query.data, moneda],
  );

  if (!admite || disabled || cuentas.length === 0 || manual) {
    return (
      <div className="space-y-1">
        <Input
          id={id}
          aria-invalid={invalid}
          placeholder="Opcional"
          disabled={!admite || disabled}
          value={cuenta ?? ''}
          onChange={(e) => onCuenta(e.target.value === '' ? null : e.target.value)}
        />
        {admite && !disabled && cuentas.length > 0 && (
          <Button
            type="button"
            variant="link"
            size="sm"
            className="h-auto p-0 text-xs"
            onClick={() => {
              setManual(false);
              onCuenta(null);
            }}
          >
            Elegir una cuenta de Millet
          </Button>
        )}
      </div>
    );
  }

  return (
    <Select
      id={id}
      aria-invalid={invalid}
      value={cuentaBancariaId ?? ''}
      onValueChange={(v) => {
        if (v === MANUAL) {
          setManual(true);
          onCuentaBancariaId(null);
          return;
        }
        onCuenta(null);
        onCuentaBancariaId(v === '' ? null : v);
      }}
    >
      <SelectTrigger aria-label="Cuenta beneficiaria" className="w-full">
        <SelectValue placeholder="Opcional · elige una cuenta de Millet" />
      </SelectTrigger>
      <SelectContent>
        {cuentas.map((c) => (
          <SelectItem key={c.id} value={c.id}>
            <span className="flex items-center gap-2">
              <span>{c.banco}</span>
              <span className="font-mono text-xs text-ink-muted">
                {enmascararCuenta(c.clabe ?? c.numeroCuenta)}
              </span>
              <span className="text-xs text-ink-muted">{c.moneda}</span>
            </span>
          </SelectItem>
        ))}
        <SelectItem value={MANUAL}>Capturar otra cuenta</SelectItem>
      </SelectContent>
    </Select>
  );
}
