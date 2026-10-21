import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { CuentaActivoSelector } from '../components/CuentaActivoSelector';
import { useGuardarActivo } from '../api/hooks';
import { useLibroQuery, type Configuracion } from '../api/libro';
const inicial: Configuracion = {
  version: 0,
  revalorizacionHabilitada: false,
  cuentaUtilidadId: null,
  cuentaPerdidaId: null,
  cuentaVentaPuenteId: null,
  cuentaRevalorizacionId: null,
};
export function ConfiguracionLibroPage() {
  const q = useLibroQuery<Configuracion | null>('configuracion-libro');
  const [edicion, setEdicion] = useState<Configuracion | null>(null);
  const guardar = useGuardarActivo();
  const datos = edicion ?? q.data ?? inicial;
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Configuración del libro"
        descripcion="Defina las cuentas para bajas, ventas y ajustes autorizados."
      />
      <form
        className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat"
        onSubmit={(e) => {
          e.preventDefault();
          guardar.mutate(
            { ruta: 'configuracion-libro', method: 'PUT', version: datos.version, body: datos },
            { onSuccess: () => setEdicion(null) },
          );
        }}
      >
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={datos.revalorizacionHabilitada}
            onChange={(e) => setEdicion({ ...datos, revalorizacionHabilitada: e.target.checked })}
          />
          Habilitar revalorización
        </label>
        <p className="text-ink-muted">
          Permanece apagada hasta que Millet valide su uso. El puente de venta debe conciliarse con
          la contabilización de la factura.
        </p>
        {(
          [
            ['cuentaUtilidadId', 'Utilidad en venta'],
            ['cuentaPerdidaId', 'Pérdida en baja o venta'],
            ['cuentaVentaPuenteId', 'Puente de venta'],
            ['cuentaRevalorizacionId', 'Contrapartida de revalorización'],
          ] as const
        ).map(([campo, label]) => (
          <div key={campo}>
            <p className="mb-1 text-xs">{label}</p>
            <CuentaActivoSelector
              value={datos[campo]}
              onChange={(v) => setEdicion({ ...datos, [campo]: v })}
            />
          </div>
        ))}
        {(q.error || guardar.error) && (
          <p role="alert" className="text-danger-fg">
            {q.error?.message || guardar.error?.message}
          </p>
        )}
        <Button
          disabled={q.isLoading || guardar.isPending}
          title={
            q.isLoading
              ? 'Cargando configuración'
              : guardar.isPending
                ? 'Guardando configuración'
                : undefined
          }
        >
          Guardar configuración
        </Button>
      </form>
    </main>
  );
}
