import { useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { useValidarMovimiento } from '../api/hooks';
import type { OrigenMovimiento } from '../api/types';
import { SELECT_CLASS } from '../lib/estilos';
import { ETIQUETA_ORIGEN, textoValidacion } from '../lib/textos';

const ORIGENES = Object.keys(ETIQUETA_ORIGEN) as OrigenMovimiento[];

/**
 * Demostración de «movimiento directo» (aún no hay pólizas): consulta el mismo puerto que usarán los módulos
 * (POST /cuentas/validar-movimiento, permiso de lectura) y explica el resultado. No registra nada.
 */
export function ProbarMovimiento({ cuentaId }: { cuentaId: string }) {
  const [origen, setOrigen] = useState<OrigenMovimiento>('Manual');
  const validar = useValidarMovimiento();
  const r = validar.data;
  return (
    <section aria-label="Probar si la cuenta acepta movimientos" className="space-y-2 rounded-lg bg-surface-card p-4 shadow-card-flat" data-print="hidden">
      <h2 className="text-sm font-semibold">Probar si la cuenta acepta movimientos</h2>
      <p className="text-xs text-ink-muted">Simula quién intenta registrar un movimiento en esta cuenta. Es solo una consulta: no registra nada.</p>
      <div className="flex flex-wrap items-end gap-2">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="probar-origen">Origen del movimiento</Label>
          <select
            id="probar-origen"
            className={SELECT_CLASS}
            value={origen}
            onChange={(e) => { setOrigen(e.target.value as OrigenMovimiento); validar.reset(); }}
          >
            {ORIGENES.map((o) => <option key={o} value={o}>{ETIQUETA_ORIGEN[o]}</option>)}
          </select>
        </div>
        <Button variant="outline" onClick={() => validar.mutate({ cuentaId, origen })} disabled={validar.isPending}>
          {validar.isPending ? 'Probando…' : 'Probar'}
        </Button>
      </div>
      {validar.isError && <p role="alert" className="text-sm text-danger-fg">No se pudo hacer la prueba (sin conexión o error inesperado). Reintenta.</p>}
      {r && (
        <p role="status" className="flex flex-wrap items-center gap-2 text-sm">
          <Badge variant={r.valida ? 'success' : 'danger'}>{r.valida ? 'Acepta' : 'Rechaza'}</Badge>
          <span>{textoValidacion(r, origen)}</span>
        </p>
      )}
    </section>
  );
}
