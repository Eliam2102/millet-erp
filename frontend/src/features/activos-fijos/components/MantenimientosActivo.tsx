import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ProveedorSelector } from '@/components/erp/selectors/ProveedorSelector';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { formatDate } from '@/lib/datetime';
import { useGuardarActivo } from '../api/hooks';
import { useConservacion, type Mantenimiento } from '../api/conservacion';
import { dinero, type Activo } from '../api/types';
export function MantenimientosActivo({ activo }: { activo: Activo }) {
  const permiso = useHasPermission(P.ActivosFijosMantenimientosGestionar);
  const query = useConservacion<Mantenimiento[]>(`activos/${activo.id}/mantenimientos`);
  const [proveedor, setProveedor] = useState<string | null>(null);
  const [q, setQ] = useState('');
  const [pagina, setPagina] = useState(0);
  const facturas = useConservacion<{ id: string; referencia: string }[]>(
    `activos/${activo.id}/facturas-mantenimiento?q=${encodeURIComponent(q)}`,
    permiso,
  );
  const guardar = useGuardarActivo();
  return (
    <section className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold">Mantenimientos</h2>
      <p className="text-ink-muted">
        Este historial no aumenta el valor del activo. El gasto se registra en Cuentas por pagar con
        la dimensión equipo.
      </p>
      {query.isPending && <p>Cargando mantenimientos…</p>}
      {query.error && <p role="alert">{query.error.message}</p>}
      {query.data?.length === 0 && <p>Sin mantenimientos registrados.</p>}
      {query.data?.slice(pagina * 10, pagina * 10 + 10).map((h) => (
        <article key={h.id} className="space-y-2 border-b border-line-row py-3">
          <p>
            {formatDate(h.fecha)} · {h.tipo === 0 ? 'Preventivo' : 'Correctivo'} · {h.descripcion}
          </p>
          <p className="tabular-nums">
            Costo: {dinero(h.costo)} · Próximo:{' '}
            {h.proximo ? formatDate(h.proximo) : 'Sin programar'}
          </p>
          <ProveedorSelector value={h.proveedorId} onChange={() => {}} disabled />
          <p>
            Factura: {h.facturaProveedorId ? 'Ligada en Cuentas por pagar' : 'Sin factura ligada'}
          </p>
        </article>
      ))}
      <div className="flex gap-2">
        <Button
          variant="outline"
          disabled={pagina === 0}
          title="Página anterior"
          onClick={() => setPagina(pagina - 1)}
        >
          Anterior
        </Button>
        <Button
          variant="outline"
          disabled={(pagina + 1) * 10 >= (query.data?.length ?? 0)}
          title="Página siguiente"
          onClick={() => setPagina(pagina + 1)}
        >
          Siguiente
        </Button>
      </div>
      {permiso && (
        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            guardar.mutate({
              ruta: `activos/${activo.id}/mantenimientos`,
              version: activo.version,
              body: {
                fecha: f.get('fecha'),
                tipo: Number(f.get('tipo')),
                proveedorId: proveedor,
                descripcion: f.get('descripcion'),
                costo: Number(f.get('costo')),
                facturaProveedorId: f.get('factura') || null,
                proximo: f.get('proximo') || null,
              },
            });
          }}
        >
          <FormGrid>
            <FormCampo span={6}>
              <label className="text-xs">
                Tipo *
                <select
                  name="tipo"
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                >
                  <option value="0">Preventivo</option>
                  <option value="1">Correctivo</option>
                </select>
              </label>
            </FormCampo>
            <FormCampo span={6}>
              <p className="text-xs">Proveedor *</p>
              <ProveedorSelector value={proveedor} onChange={setProveedor} />
            </FormCampo>
            {(
              [
                ['fecha', 'Fecha', 'date'],
                ['descripcion', 'Descripción', 'text'],
                ['costo', 'Costo', 'number'],
                ['proximo', 'Próximo mantenimiento', 'date'],
              ] as const
            ).map(([name, label, type]) => (
              <FormCampo key={name} span={6}>
                <label className="text-xs">
                  {label}
                  {name !== 'proximo' ? ' *' : ''}
                  <Input
                    name={name}
                    type={type}
                    required={name !== 'proximo'}
                    min={type === 'number' ? 0 : undefined}
                    step={type === 'number' ? '0.01' : undefined}
                    maxLength={2000}
                  />
                </label>
              </FormCampo>
            ))}
            <FormCampo span={6}>
              <label className="text-xs">
                Buscar factura por folio
                <Input value={q} onChange={(e) => setQ(e.target.value)} />
              </label>
            </FormCampo>
            <FormCampo span={6}>
              <label className="text-xs">
                Factura del proveedor
                <select
                  name="factura"
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                >
                  <option value="">Sin factura ligada</option>
                  {facturas.data?.map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.referencia}
                    </option>
                  ))}
                </select>
              </label>
            </FormCampo>
          </FormGrid>
          {facturas.error && <p role="alert">{facturas.error.message}</p>}
          <Button
            type="submit"
            disabled={!proveedor || guardar.isPending}
            title="Seleccione proveedor y complete los datos"
          >
            Registrar mantenimiento
          </Button>
        </form>
      )}
      {guardar.error && (
        <p role="alert" className="text-danger-fg">
          {guardar.error.message}
        </p>
      )}
      {guardar.isSuccess && <p role="status">Mantenimiento registrado.</p>}
    </section>
  );
}
