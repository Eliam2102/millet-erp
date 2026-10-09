import { useState } from 'react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import {
  useActualizarCuentaBancaria,
  useCrearCuentaBancaria,
  useRegistrarSaldoInicial,
} from '@/features/tesoreria/api/useTesoreria';
import type { CuentaSaldoResponse } from '@/features/tesoreria/api/types';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export interface CuentaBancariaSheetProps {
  /** true = sheet visible. En modo edición además viene `cuenta`. */
  open: boolean;
  /** null = alta; con valor = edición de esa cuenta. */
  cuenta: CuentaSaldoResponse | null;
  onOpenChange: (open: boolean) => void;
}

const CLABE_REGEX = /^\d{18}$/;
const MONEDA_REGEX = /^[A-Za-z]{3}$/;

/**
 * Sheet "Nueva cuenta bancaria" / "Editar cuenta" (TES-7 revisada, P4).
 * En edición el número de cuenta es inmutable (identidad natural, único
 * por empresa) y la CLABE es write-only: el catálogo llega enmascarado
 * salvo `ver-cuenta-completa`, así que el form solo permite capturar una
 * CLABE nueva (vacío = sin cambio) o quitarla, nunca re-enviar la actual.
 */
export function CuentaBancariaSheet({ open, cuenta, onOpenChange }: CuentaBancariaSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-md">
        {/* key remonta el form al cambiar de cuenta (o alta): el estado
            inicial se deriva de props sin efectos. */}
        {open && (
          <CuentaBancariaForm
            key={cuenta?.id ?? 'nueva'}
            cuenta={cuenta}
            onOpenChange={onOpenChange}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function CuentaBancariaForm({
  cuenta,
  onOpenChange,
}: {
  cuenta: CuentaSaldoResponse | null;
  onOpenChange: (open: boolean) => void;
}) {
  const crear = useCrearCuentaBancaria();
  const actualizar = useActualizarCuentaBancaria();
  const registrarSaldo = useRegistrarSaldoInicial();
  const keyFor = useBodyScopedIdempotencyKey();
  const [saldo, setSaldo] = useState('');
  const [corte, setCorte] = useState('');
  const [motivoSaldo, setMotivoSaldo] = useState('');
  const [inventario, setInventario] = useState({ sucursal: cuenta?.sucursal ?? '', finalidad: cuenta?.finalidad ?? '', titular: cuenta?.titular ?? '', firmantes: cuenta?.firmantes ?? '' });
  const esEdicion = cuenta != null;
  const pendiente = crear.isPending || actualizar.isPending || registrarSaldo.isPending;

  const [banco, setBanco] = useState(cuenta?.banco ?? '');
  const [numeroCuenta, setNumeroCuenta] = useState(cuenta?.numeroCuenta ?? '');
  const [clabe, setClabe] = useState('');
  const [limpiarClabe, setLimpiarClabe] = useState(false);
  const [moneda, setMoneda] = useState(cuenta?.moneda ?? 'MXN');
  const [cuentaContableRef, setCuentaContableRef] = useState(cuenta?.cuentaContableRef ?? '');
  const [perfilExtracto, setPerfilExtracto] = useState(cuenta?.perfilExtracto ?? '');

  const clabeValida = clabe === '' || CLABE_REGEX.test(clabe.trim());
  const valido =
    banco.trim() !== '' &&
    (esEdicion || numeroCuenta.trim() !== '') &&
    MONEDA_REGEX.test(moneda.trim()) &&
    clabeValida &&
    !(limpiarClabe && clabe !== '');

  function onError(error: unknown, titulo: string) {
    toast.error(esApiError(error) ? error.problem.title : titulo, {
      description: esApiError(error)
        ? (error.problem.detail ?? `Código: ${error.traceId}`)
        : undefined,
    });
  }

  function confirmar() {
    if (!valido) return;
    if (!esEdicion) {
      crear.mutate(
        {
          command: {
            ...inventario,
            banco: banco.trim(),
            numeroCuenta: numeroCuenta.trim(),
            clabe: clabe.trim() === '' ? undefined : clabe.trim(),
            moneda: moneda.trim().toUpperCase(),
            cuentaContableRef:
              cuentaContableRef.trim() === '' ? undefined : cuentaContableRef.trim(),
            perfilExtracto: perfilExtracto.trim() === '' ? undefined : perfilExtracto.trim(),
          },
          idempotencyKey: crypto.randomUUID(),
        },
        {
          onSuccess: (creada) => {
            toast.success('Cuenta bancaria creada', {
              description: `${creada.banco} · ${creada.numeroCuenta} (${creada.moneda}).`,
            });
            onOpenChange(false);
          },
          onError: (error) => onError(error, 'No se pudo crear la cuenta'),
        },
      );
      return;
    }
    actualizar.mutate(
      {
        cuentaId: cuenta.id,
        body: {
          ...inventario,
          banco: banco.trim(),
          moneda: moneda.trim().toUpperCase(),
          clabe: clabe.trim() === '' ? undefined : clabe.trim(),
          limpiarClabe,
          cuentaContableRef: cuentaContableRef.trim() === '' ? undefined : cuentaContableRef.trim(),
          perfilExtracto: perfilExtracto.trim() === '' ? undefined : perfilExtracto.trim(),
        },
        versionEsperada: cuenta.version,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (editada) => {
          toast.success('Cuenta bancaria actualizada', {
            description: `${editada.banco} · ${editada.numeroCuenta}.`,
          });
          onOpenChange(false);
        },
        onError: (error) => onError(error, 'No se pudo actualizar la cuenta'),
      },
    );
  }

  return (
    <>
      <SheetHeader>
        <SheetTitle>{esEdicion ? 'Editar cuenta bancaria' : 'Nueva cuenta bancaria'}</SheetTitle>
        <SheetDescription>
          {esEdicion
            ? `${cuenta.banco} · ${cuenta.numeroCuenta} (${cuenta.moneda}).`
            : 'Cuenta propia de la empresa para operar pagos, depósitos y conciliación.'}
        </SheetDescription>
      </SheetHeader>

      <div className="space-y-4 px-4 py-4">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="cta-banco">
            Banco
          </label>
          <Input
            id="cta-banco"
            value={banco}
            onChange={(e) => setBanco(e.target.value)}
            placeholder="BBVA México"
            maxLength={120}
          />
        </div>

        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="cta-numero">
            Número de cuenta
          </label>
          <Input
            id="cta-numero"
            value={numeroCuenta}
            onChange={(e) => setNumeroCuenta(e.target.value)}
            placeholder="0123456789"
            className="font-mono"
            maxLength={40}
            disabled={esEdicion}
          />
          {esEdicion && (
            <p className="text-xs text-muted-foreground">
              El número de cuenta es inmutable: identifica la cuenta en toda la operación. Si se
              capturó mal, desactívala y crea una nueva.
            </p>
          )}
        </div>

        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="cta-clabe">
            {esEdicion ? 'Nueva CLABE (vacío = sin cambio)' : 'CLABE (opcional)'}
          </label>
          <Input
            id="cta-clabe"
            value={clabe}
            onChange={(e) => setClabe(e.target.value)}
            placeholder="032180000118359719"
            className="font-mono"
            maxLength={18}
            disabled={limpiarClabe}
          />
          {!clabeValida && (
            <p className="text-xs text-danger-fg">
              La CLABE son 18 dígitos (se valida el dígito de control al guardar).
            </p>
          )}
          {esEdicion && cuenta.clabe != null && (
            <label className="flex cursor-pointer items-center gap-2 pt-1 text-xs">
              <Checkbox
                checked={limpiarClabe}
                onCheckedChange={(v) => setLimpiarClabe(v === true)}
              />
              Quitar la CLABE registrada ({cuenta.clabe})
            </label>
          )}
        </div>

        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="cta-moneda">
            Moneda (ISO 4217)
          </label>
          <Input
            id="cta-moneda"
            value={moneda}
            onChange={(e) => setMoneda(e.target.value.toUpperCase())}
            placeholder="MXN"
            className="w-24 font-mono uppercase"
            maxLength={3}
          />
          {esEdicion && (
            <p className="text-xs text-muted-foreground">
              Solo se puede cambiar mientras la cuenta no tenga movimientos.
            </p>
          )}
        </div>

        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="cta-contable">
            Cuenta contable (opcional)
          </label>
          <Input
            id="cta-contable"
            value={cuentaContableRef}
            onChange={(e) => setCuentaContableRef(e.target.value)}
            placeholder="1102-001"
            className="font-mono"
            maxLength={40}
          />
        </div>

        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="cta-perfil">
            Perfil de extracto (opcional)
          </label>
          <Input
            id="cta-perfil"
            value={perfilExtracto}
            onChange={(e) => setPerfilExtracto(e.target.value)}
            placeholder="bbva-csv"
            className="font-mono"
            maxLength={40}
          />
          <p className="text-xs text-muted-foreground">
            Parser del extracto para conciliación bancaria; se configura al habilitar la
            conciliación de esta cuenta.
          </p>
        </div>

        {(['sucursal', 'finalidad', 'titular', 'firmantes'] as const).map(campo => (
          <div key={campo} className="space-y-1">
            <label htmlFor={`cta-${campo}`} className="text-xs text-ink-muted">{{ sucursal: 'Sucursal', finalidad: 'Finalidad', titular: 'Titular', firmantes: 'Firmantes' }[campo]}</label>
            <Input id={`cta-${campo}`} value={inventario[campo]} maxLength={campo === 'firmantes' ? 1000 : campo === 'finalidad' ? 400 : campo === 'titular' ? 200 : 120}
              onChange={e => setInventario({ ...inventario, [campo]: e.target.value })} />
          </div>
        ))}
        {cuenta && <section className="space-y-2 rounded-lg bg-surface-subtle p-3">
          <h2 className="text-sm font-semibold">Saldo inicial</h2>
          {cuenta.saldoInicial != null ? <p className="text-sm tabular-nums">{cuenta.saldoInicial.toLocaleString('es-MX', { style: 'currency', currency: cuenta.moneda })} · Corte: {cuenta.fechaCorteSaldoInicial}</p> : <>
            <p className="text-xs text-ink-muted">Captura única. El saldo corresponde al cierre del día de corte, anterior al primer movimiento.</p>
            <label htmlFor="cta-saldo" className="text-xs">Saldo al corte</label>
            <Input id="cta-saldo" type="number" step="0.01" value={saldo} onChange={e => setSaldo(e.target.value)} />
            <label htmlFor="cta-corte" className="text-xs">Fecha de corte</label>
            <Input id="cta-corte" type="date" value={corte} onChange={e => setCorte(e.target.value)} />
            <label htmlFor="cta-motivo-saldo" className="text-xs">Motivo y referencia del saldo</label>
            <Input id="cta-motivo-saldo" maxLength={400} value={motivoSaldo} onChange={e => setMotivoSaldo(e.target.value)} />
            <Button variant="outline" disabled={registrarSaldo.isPending || saldo === '' || !Number.isFinite(Number(saldo)) || !corte || !motivoSaldo.trim()} onClick={() => {
              const command = { cuentaId: cuenta.id, saldo: Number(saldo), fechaCorte: corte, motivo: motivoSaldo.trim(), version: cuenta.version };
              registrarSaldo.mutate({ ...command, idempotencyKey: keyFor(command) }, {
                onSuccess: () => { toast.success('Saldo inicial registrado'); onOpenChange(false); },
                onError: e => onError(e, 'No se pudo registrar el saldo inicial'),
              });
            }}>Registrar saldo inicial</Button>
          </>}
        </section>}
        <div className="flex justify-end gap-2 pt-2">
          <Button variant="ghost" onClick={() => onOpenChange(false)} disabled={pendiente}>
            Cancelar
          </Button>
          <Button onClick={confirmar} disabled={pendiente || !valido}>
            {pendiente ? 'Guardando…' : esEdicion ? 'Guardar cambios' : 'Crear cuenta'}
          </Button>
        </div>
      </div>
    </>
  );
}
