import { motivosDisposicion } from '../api/motivos-disposicion';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useGuardarActivo } from '../api/hooks';
import { useLibroQuery, type Corrida, type Operacion } from '../api/libro';
import type { Activo } from '../api/types';
import { TablaLibro } from './TablaLibro';
import { columnasDepreciacion } from '../components/columnas-depreciacion';
export function DepreciacionActivo({ activo }: { activo: Activo }) {
  const q = useLibroQuery<{ depreciaciones: Corrida[]; operaciones: Operacion[] }>(
    `activos/${activo.id}/libro`,
  );
  const baja = useHasPermission(P.ActivosFijosBajasRegistrar);
  const ajuste = useHasPermission(P.ActivosFijosAjustesRegistrar);
  const autorizarBaja = useHasPermission(P.ActivosFijosBajasAutorizar);
  const autorizarAjuste = useHasPermission(P.ActivosFijosAjustesAutorizar);
  const [tipo, setTipo] = useState('');
  const [evidencia, setEvidencia] = useState<string | null>(null);
  const [error, setError] = useState('');
  const guardar = useGuardarActivo();
  return (
    <div className="space-y-5">
      <div className="flex gap-3">
        {baja && activo.estado === 1 && (
          <Button variant="outline" onClick={() => setTipo('Baja')}>
            Dar de baja
          </Button>
        )}
        {ajuste && activo.estado === 1 && (
          <Button variant="outline" onClick={() => setTipo('Amortizacion')}>
            Ajuste
          </Button>
        )}
      </div>
      {tipo && (
        <form
          className="grid gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat md:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            guardar.mutate(
              {
                ruta: `activos/${activo.id}/operaciones`,
                version: activo.version,
                body: {
                  tipo,
                  fecha: f.get('fecha'),
                  importe: Number(f.get('importe') ?? 0),
                  motivo: f.get('motivo'),
                  solicitadoPor: f.get('solicitadoPor'),
                  evidencia,
                },
              },
              {
                onSuccess: () => {
                  setTipo('');
                  setEvidencia(null);
                },
              },
            );
          }}
        >
          {tipo !== 'Baja' && (
            <label>
              Tipo de ajuste *
              <select
                className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3"
                value={tipo}
                onChange={(e) => setTipo(e.target.value)}
              >
                <option value="Amortizacion">Amortización manual</option>
                <option value="Revalorizacion">Revalorización</option>
              </select>
            </label>
          )}
          <label>
            Fecha *<Input name="fecha" type="date" required />
          </label>
          {tipo !== 'Baja' && (
            <label>
              Importe con signo *<Input name="importe" type="number" step="0.01" required />
            </label>
          )}
          <label>
            Motivo *
            {tipo === 'Baja' ? (
              <select
                name="motivo"
                required
                className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3"
                defaultValue=""
              >
                <option value="">Seleccione motivo</option>
                {motivosDisposicion.map((m) => (
                  <option key={m.codigo} value={m.codigo}>
                    {m.etiqueta}
                  </option>
                ))}
              </select>
            ) : (
              <Input
                name="motivo"
                required
                placeholder={tipo === 'Baja' ? 'Seleccione motivo' : 'Explique la corrección'}
              />
            )}
          </label>
          {tipo === 'Baja' && (
            <label>
              Solicitado por (área usuaria) *<Input name="solicitadoPor" required maxLength={200} />
            </label>
          )}
          {tipo === 'Baja' && (
            <label>
              Evidencia PDF o imagen *
              <Input
                type="file"
                required
                accept="application/pdf,image/png,image/jpeg"
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  setEvidencia(null);
                  setError('');
                  if (!f) return;
                  if (f.size > 2_000_000) {
                    setError('Adjunte un archivo menor de 2 MB.');
                    return;
                  }
                  const reader = new FileReader();
                  reader.onload = () => setEvidencia(String(reader.result));
                  reader.onerror = () => setError('No se pudo leer el adjunto.');
                  reader.readAsDataURL(f);
                }}
              />
            </label>
          )}
          <p className="text-ink-muted md:col-span-2">
            La baja la registra el Contador General y la autoriza el Director de Administración y
            Finanzas, otra persona. La revalorización requiere habilitación en la configuración del
            libro.
          </p>
          <Button
            type="submit"
            disabled={guardar.isPending || (tipo === 'Baja' && !evidencia)}
            title={
              guardar.isPending
                ? 'Guardando solicitud'
                : tipo === 'Baja' && !evidencia
                  ? 'Adjunte la evidencia'
                  : undefined
            }
          >
            Solicitar autorización
          </Button>
          <Button type="button" variant="outline" onClick={() => setTipo('')}>
            Cancelar
          </Button>
        </form>
      )}
      {(error || q.error || guardar.error) && (
        <p role="alert" className="text-danger-fg">
          {error || q.error?.message || guardar.error?.message}
        </p>
      )}
      <TablaLibro
        titulo="Depreciación del activo"
        filas={q.data?.depreciaciones.flatMap((c) =>
          c.detalles.map((d) => ({
            ...d,
            periodo: c.periodo,
            estado: c.anulada ? 'Anulada' : 'Confirmada',
          })),
        )}
        columnas={[
          { clave: 'periodo', etiqueta: 'Periodo', tipo: 'fecha' },
          ...columnasDepreciacion,
          { clave: 'estado', etiqueta: 'Estado', tipo: 'texto' },
        ]}
      />
      <TablaLibro
        titulo="Bajas y ajustes"
        filas={q.data?.operaciones}
        columnas={[
          { clave: 'tipo', etiqueta: 'Tipo', tipo: 'texto' },
          { clave: 'fecha', etiqueta: 'Fecha', tipo: 'fecha' },
          { clave: 'motivo', etiqueta: 'Motivo', tipo: 'texto' },
          { clave: 'solicitadoPor', etiqueta: 'Solicitado por', tipo: 'texto' },
          { clave: 'importe', etiqueta: 'Importe', tipo: 'moneda' },
        ]}
        accionFila={(o) => (
          <div className="flex items-center gap-2">
            {o.evidencia && (
              <a href={o.evidencia} download="evidencia-baja" className="text-brand">
                Descargar evidencia
              </a>
            )}
            {o.polizaId ? (
              <Badge variant={o.anulada ? 'neutral' : 'success'}>
                {o.anulada ? 'Anulada' : 'Autorizada'}
              </Badge>
            ) : (o.tipo === 'Baja' ? autorizarBaja : autorizarAjuste) ? (
              <Button
                variant="outline"
                disabled={guardar.isPending}
                title={guardar.isPending ? 'Guardando autorización' : undefined}
                onClick={() =>
                  guardar.mutate({
                    ruta: `operaciones/${o.id}/autorizar`,
                    body: {},
                    version: o.version,
                  })
                }
              >
                Autorizar
              </Button>
            ) : (
              <Badge variant="warning">Pendiente de autorización</Badge>
            )}
          </div>
        )}
      />
    </div>
  );
}
