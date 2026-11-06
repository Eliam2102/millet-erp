import { useCuenta } from '@/features/contabilidad/api/hooks';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useGuardarActivo } from '../api/hooks';
import { useLibroQuery } from '../api/libro';
import type { Clase } from '../api/types';
function CuentaVersion({ id }: { id: string | null }) {
  const q = useCuenta(id);
  const cuenta = q.data?.data;
  return (
    <span>
      {cuenta
        ? `${cuenta.codigo} · ${cuenta.nombre}`
        : q.isLoading
          ? 'Cargando cuenta…'
          : q.error
            ? 'Error al consultar la cuenta'
            : 'Sin cuenta'}
    </span>
  );
}
export function VersionesClase({ clase }: { clase: Clase }) {
  const guardar = useGuardarActivo();
  const autorizar = useHasPermission(P.ActivosFijosClasesAutorizar);
  const usuario = useAuthStore((s) => s.user?.id);
  const [motivo, setMotivo] = useState('');
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="font-semibold">Versiones de {clase.clave}</h2>
      {clase.versiones?.map((v) => (
        <div key={v.id} className="space-y-2 border-b border-line-control pb-3">
          <p>
            Versión {v.numero} · {v.vigencia === '0001-01-01' ? 'Inicial' : `Desde ${v.vigencia}`} ·
            Contable {v.tasaContableAnual ?? 'Sin tasa'} % / fiscal{' '}
            {v.tasaFiscalAnual ?? 'Sin tasa'} %
          </p>
          <Badge variant={v.estado === 0 ? 'warning' : v.estado === 1 ? 'success' : 'danger'}>
            {['En revisión', 'Vigente', 'Rechazada'][v.estado]}
          </Badge>
          <p>
            {v.depreciable ? 'Depreciable' : 'No depreciable'} · Cuentas de activo / acumulada /
            gasto / puente:{' '}
            {[v.cuentaActivoId, v.cuentaDepreciacionId, v.cuentaGastoId, v.cuentaPuenteId].map(
              (id, i) => (
                <span key={i}>
                  <CuentaVersion id={id} />
                  {i < 3 ? ' / ' : ''}
                </span>
              ),
            )}
          </p>
          {v.motivoCambio && <p>Motivo del cambio: {v.motivoCambio}</p>}
          {v.motivoRechazo && <p>Motivo: {v.motivoRechazo}</p>}
          {v.estado === 0 && autorizar && (
            <div className="space-y-2">
              {v.propuestoPor === usuario ? (
                <p>La propuesta debe resolverla otra persona.</p>
              ) : (
                <>
                  <label>
                    Motivo de rechazo
                    <Input
                      maxLength={500}
                      value={motivo}
                      onChange={(e) => setMotivo(e.target.value)}
                    />
                  </label>
                  <div className="flex gap-3">
                    {[true, false].map((aceptar) => (
                      <Button
                        key={String(aceptar)}
                        variant="outline"
                        disabled={guardar.isPending || (!aceptar && !motivo.trim())}
                        onClick={() =>
                          guardar.mutate({
                            ruta: `clases/${clase.id}/versiones/${v.id}/resolver`,
                            version: v.version,
                            body: { autorizar: aceptar, motivo },
                          })
                        }
                      >
                        {aceptar ? 'Autorizar versión' : 'Rechazar versión'}
                      </Button>
                    ))}
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      ))}
      {!clase.versiones?.length && <p>La configuración actual es la versión inicial.</p>}
      {guardar.error && (
        <p role="alert" className="text-danger-fg">
          {guardar.error.message}
        </p>
      )}
    </section>
  );
}
export function EquivalenciasSap() {
  const q = useLibroQuery<{ claseSap: string; claveErp: string }[]>('clases/equivalencias-sap');
  const guardar = useGuardarActivo();
  const [sap, setSap] = useState('');
  const [erp, setErp] = useState('');
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="font-semibold">Equivalencias de clases SAP</h2>
      {q.data?.map((e) => (
        <Button
          key={e.claseSap}
          variant="ghost"
          onClick={() => {
            setSap(e.claseSap);
            setErp(e.claveErp);
          }}
        >
          {e.claseSap} → {e.claveErp}
        </Button>
      ))}
      <form
        className="flex flex-wrap items-end gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          guardar.mutate({
            ruta: 'clases/equivalencias-sap',
            method: 'PUT',
            body: { claseSap: sap, claveErp: erp },
          });
        }}
      >
        <label>
          Clase SAP
          <Input required maxLength={100} value={sap} onChange={(e) => setSap(e.target.value)} />
        </label>
        <label>
          Clave ERP
          <Input required maxLength={20} value={erp} onChange={(e) => setErp(e.target.value)} />
        </label>
        <Button type="submit" disabled={guardar.isPending}>
          Guardar equivalencia
        </Button>
      </form>
      {(q.error || guardar.error) && (
        <p role="alert" className="text-danger-fg">
          {(q.error || guardar.error)?.message}
        </p>
      )}
    </section>
  );
}
