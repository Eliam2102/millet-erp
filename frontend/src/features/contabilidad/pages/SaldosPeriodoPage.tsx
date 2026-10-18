import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ArrowLeft, Scale } from 'lucide-react';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useEjercicios } from '../api/periodos';
import { useSaldosPeriodo } from '../api/polizas';
import { CuentaSelector, type CuentaOpcion } from '../components/dimensiones/CuentaSelector';
import { SELECT_CLASS } from '../lib/estilos';
import { centavos, etiquetaPeriodo, mensajePoliza, monto } from '../lib/polizas';

/**
 * Movimientos del periodo por cuenta (F1-CON-04): cargos, abonos y saldo de las pólizas autorizadas, anuladas y sus
 * anulaciones (sin borradores). Una póliza anulada y su anulación se compensan: la cuenta queda en cero (K10.3).
 */
export function SaldosPeriodoPage() {
  const ejercicios = useEjercicios({ retry: 0 });
  const periodos = useMemo(
    () => (ejercicios.data ?? []).flatMap((e) => e.periodos).sort((a, b) => b.anio - a.anio || b.numero - a.numero),
    [ejercicios.data],
  );
  const [elegido, setElegido] = useState('');
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  // Por omisión, el mes en curso; si no existe, el periodo abierto más reciente.
  const hoy = new Date();
  const porDefecto =
    periodos.find((p) => p.anio === hoy.getFullYear() && p.numero === hoy.getMonth() + 1) ??
    periodos.find((p) => p.estado === 'Abierto') ??
    periodos[0];
  const valor = elegido || (porDefecto ? `${porDefecto.anio}-${porDefecto.numero}` : '');
  const [anio, numero] = valor ? valor.split('-').map(Number) : [null, null];
  const saldos = useSaldosPeriodo(anio, numero, cuenta?.id ?? '');
  const filas = saldos.data ?? [];
  const totales = {
    cargos: centavos(filas.reduce((s, f) => s + f.cargos, 0)),
    abonos: centavos(filas.reduce((s, f) => s + f.abonos, 0)),
    saldo: centavos(filas.reduce((s, f) => s + f.saldo, 0)),
  };

  return (
    <div className="flex flex-col gap-4 px-4 py-5 sm:px-6">
      <header className="flex flex-wrap items-start gap-3">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2 text-3xl font-semibold">
            <Scale className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Movimientos del periodo por cuenta
          </h1>
          <p className="text-sm text-ink-muted">
            Cargos, abonos y saldo por cuenta de las pólizas autorizadas del periodo. Una póliza anulada y su anulación se compensan.
          </p>
        </div>
        <Button asChild variant="secondary" className="ml-auto" data-print="hidden">
          <Link to="/contabilidad/polizas"><ArrowLeft className="mr-1 size-3.5" aria-hidden="true" />Pólizas</Link>
        </Button>
      </header>

      <section className="flex flex-wrap items-end gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat" data-print="hidden">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="saldos-periodo">Periodo</Label>
          <select id="saldos-periodo" className={SELECT_CLASS} value={valor} onChange={(e) => setElegido(e.target.value)}>
            {periodos.length === 0 && <option value="">{ejercicios.isLoading ? 'Cargando…' : 'Sin periodos'}</option>}
            {periodos.map((p) => (
              <option key={p.id} value={`${p.anio}-${p.numero}`}>
                {etiquetaPeriodo(p.anio, p.numero)}{p.estado === 'Abierto' ? ' (abierto)' : p.estado === 'Cerrado' ? ' (cerrado)' : ''}
              </option>
            ))}
          </select>
        </div>
        <div className="flex w-full flex-col gap-1.5 sm:w-96">
          <Label htmlFor="saldos-cuenta">Cuenta <span className="font-normal text-ink-muted">(opcional)</span></Label>
          <CuentaSelector id="saldos-cuenta" value={cuenta} onChange={setCuenta} soloAfectables vacio="Todas las cuentas" />
        </div>
      </section>

      <section aria-label="Movimientos por cuenta" className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
        {!anio ? (
          <p className="p-6 text-sm text-ink-muted">Elige un periodo.</p>
        ) : saldos.isLoading ? (
          <div className="p-4"><Skeleton className="h-8 w-full" /></div>
        ) : saldos.isError ? (
          <p role="alert" className="p-4 text-sm text-danger-fg">No se pudieron cargar los movimientos. {mensajePoliza(saldos.error)}</p>
        ) : filas.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">El periodo no tiene pólizas autorizadas{cuenta ? ' en esta cuenta' : ''}.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-sm">
              <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <tr>
                  <th className="h-9 px-3">Cuenta</th><th className="px-3">Nombre</th>
                  <th className="px-3 text-right">Cargos</th><th className="px-3 text-right">Abonos</th><th className="px-3 text-right">Saldo</th>
                </tr>
              </thead>
              <tbody>
                {filas.map((f) => (
                  <tr key={f.cuentaId} className="h-11 border-b border-line-row">
                    <td className="px-3 font-mono text-xs">{f.cuentaCodigo}</td>
                    <td className="px-3">{f.cuentaNombre ?? ''}</td>
                    <td className="px-3 text-right tabular-nums">{monto(f.cargos)}</td>
                    <td className="px-3 text-right tabular-nums">{monto(f.abonos)}</td>
                    <td className={cn('px-3 text-right font-medium tabular-nums', f.saldo < 0 && 'text-danger-fg')}>{monto(f.saldo)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="bg-surface-subtle font-semibold">
                  <td className="px-3 py-2" colSpan={2}>Totales del periodo</td>
                  <td className="px-3 py-2 text-right tabular-nums">{monto(totales.cargos)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{monto(totales.abonos)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{monto(totales.saldo)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
