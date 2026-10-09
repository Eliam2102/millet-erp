import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useActualizarToleranciaProveedor } from '../api/proveedores';
import { interpretarToleranciaMxn } from './tolerancia-proveedor';

interface Props { proveedorId: string; montoMxn: number | null }

export function ProveedorToleranciaSection({ proveedorId, montoMxn }: Props) {
  const puedeEditar = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresToleranciaEditar);
  const [editando, setEditando] = useState(false);
  const [valor, setValor] = useState('');
  const actualizar = useActualizarToleranciaProveedor();
  const resultado = interpretarToleranciaMxn(valor);
  const campoId = `tolerancia-${proveedorId}`;

  function guardar(event: React.FormEvent) {
    event.preventDefault();
    if (!puedeEditar || !resultado.valido || actualizar.isPending) return;
    actualizar.mutate({ id: proveedorId, montoMxn: resultado.monto, idempotencyKey: crypto.randomUUID() }, {
      onSuccess: () => {
        setEditando(false);
        toast.success('Tolerancia del proveedor actualizada');
      },
      onError: (error) => toast.error('No se pudo guardar la tolerancia', {
        description: esApiError(error) ? error.problem.detail ?? error.problem.title : 'Intenta de nuevo.',
      }),
    });
  }

  return (
    <section aria-labelledby={`${campoId}-titulo`} className="mt-4 max-w-3xl rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h3 id={`${campoId}-titulo`} className="text-sm font-semibold text-ink">Tolerancia factura contra OC (MXN)</h3>
        {puedeEditar && !editando && <Button variant="ghost" size="sm" onClick={() => {
          setValor(montoMxn == null ? '' : String(montoMxn));
          setEditando(true);
        }}>Editar tolerancia</Button>}
      </div>
      <p id={`${campoId}-ayuda`} className="mb-3 text-xs text-ink-muted">
        Diferencia máxima en pesos entre la factura y la orden de compra. Si se rebasa, la factura se rechaza sin opción de forzarla.
      </p>
      {editando && puedeEditar ? (
        <form onSubmit={guardar} className="space-y-3">
          <Label htmlFor={campoId}>Tolerancia factura contra OC (MXN) <span className="font-normal text-ink-muted">(opcional)</span></Label>
          <Input id={campoId} inputMode="decimal" value={valor} onChange={(event) => setValor(event.target.value)}
            aria-describedby={`${campoId}-ayuda ${campoId}-opcional${!resultado.valido ? ` ${campoId}-error` : ''}`}
            aria-invalid={!resultado.valido} disabled={actualizar.isPending} className="text-right tabular-nums" />
          <p id={`${campoId}-opcional`} className="text-xs text-ink-muted">Deja el campo vacío para usar la tolerancia general de Administración. Cero no permite diferencias.</p>
          {!resultado.valido && <p id={`${campoId}-error`} role="alert" className="text-xs text-danger-fg">Ingresa un monto mayor o igual a cero, con hasta 14 enteros y 4 decimales. Usa punto decimal y omite separadores de miles.</p>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" size="lg" disabled={actualizar.isPending} onClick={() => setEditando(false)}>Cancelar</Button>
            <Button type="submit" size="lg" disabled={!resultado.valido || actualizar.isPending}
              title={actualizar.isPending ? 'Guardando la tolerancia' : !resultado.valido ? 'Corrige el monto para guardar' : undefined}>
              {actualizar.isPending ? 'Guardando…' : 'Guardar tolerancia'}
            </Button>
          </div>
        </form>
      ) : <p className="text-right text-sm font-medium tabular-nums text-ink">{montoMxn == null
        ? 'Usa la tolerancia general de Administración'
        : `${montoMxn.toLocaleString('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 4 })} MXN`}</p>}
    </section>
  );
}
