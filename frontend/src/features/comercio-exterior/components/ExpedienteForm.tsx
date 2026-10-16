import { useState, type FormEvent } from 'react';
import { useNavigate } from '@tanstack/react-router';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { IncotermSelector } from '@/components/erp/selectors/IncotermSelector';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { esApiError } from '@/lib/api';
import { useCrearExpediente, useOrdenesImportacion } from '../api/hooks';
import type { CrearExpediente } from '../api/types';

export function ExpedienteForm({ onClose }: { onClose: () => void }) {
  const user = useAuthStore((s) => s.user);
  const sucursal = useAuthStore((s) => s.currentSucursalId);
  const [datos, setDatos] = useState<CrearExpediente>({
    tipo: 1,
    ocId: null,
    sucursalId: sucursal,
    referencia: '',
    incotermId: null,
    moneda: null,
    responsableUsuarioId: user?.id ?? '',
    clientes: [],
    tc: null,
    fechaTc: null,
    fuenteTc: null,
  });
  const [dirty, setDirty] = useState(false);
  const navigate = useNavigate();
  const crear = useCrearExpediente();
  const [busquedaOc, setBusquedaOc] = useState('');
  const qOc = useDebouncedValue(busquedaOc, 300);
  const [ocLabel, setOcLabel] = useState('');
  const ocs = useOrdenesImportacion(datos.tipo === 1, qOc);
  const canEditar = useHasPermission(P.ComercioExteriorExpedientesEditar);
  const usuarios = useHasPermission(P.IdentidadUsuariosLeer);
  function update(patch: Partial<CrearExpediente>) {
    setDirty(true);
    setDatos((d) => ({ ...d, ...patch }));
  }
  function cerrar() {
    if (
      !crear.isPending &&
      (!dirty || window.confirm('Hay datos sin guardar. ¿Descartar la captura?'))
    )
      onClose();
  }
  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      const r = await crear.mutateAsync(datos);
      onClose();
      await navigate({
        to: '/comercio-exterior/expedientes/$id',
        params: { id: r.data.expediente.id },
      });
    } catch {
      /* El error se muestra en el formulario. */
    }
  }
  const bloqueo =
    datos.tipo === 1 ? !datos.ocId : !datos.sucursalId || !datos.moneda || !datos.clientes.length;
  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) cerrar();
      }}
    >
      <SheetContent
        className="flex w-sheet max-w-full flex-col p-0"
        overlayClassName="bg-[var(--mt-scrim-sheet)]"
      >
        <SheetHeader className="border-b border-line p-5">
          <SheetTitle>Nuevo expediente</SheetTitle>
          <SheetDescription>Captura el origen y los datos de la operación.</SheetDescription>
        </SheetHeader>
        <form onSubmit={submit} className="flex min-h-0 flex-1 flex-col">
          <div className="flex-1 space-y-6 overflow-y-auto p-5">
            <fieldset className="space-y-3">
              <legend className="text-sm font-semibold">1. Origen de la operación</legend>
              <Label htmlFor="ce-tipo">Tipo</Label>
              <select
                id="ce-tipo"
                className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm"
                value={datos.tipo}
                onChange={(e) =>
                  update({
                    tipo: Number(e.target.value) as 1 | 2,
                    ocId: null,
                    incotermId: null,
                    moneda: null,
                  })
                }
              >
                <option value={1}>Importación</option>
                <option value={2}>Exportación</option>
              </select>
              {datos.tipo === 1 ? (
                <div className="space-y-1.5">
                  <Label htmlFor="ce-buscar-oc">Buscar OC por folio</Label>
                  <Input
                    id="ce-buscar-oc"
                    value={busquedaOc}
                    onChange={(e) => setBusquedaOc(e.target.value)}
                    placeholder="Folio de la OC"
                  />
                  <Label>Orden de compra con importación</Label>
                  <CatalogoEagerCombobox
                    items={ocs.data ?? []}
                    loading={ocs.isLoading}
                    error={ocs.error}
                    value={datos.ocId}
                    onChange={(id) => {
                      const oc = ocs.data?.find((x) => x.id === id);
                      setOcLabel(oc?.folio ?? '');
                      update({
                        ocId: id,
                        sucursalId: oc?.sucursalId ?? null,
                        moneda: oc?.moneda ?? null,
                        incotermId: oc?.informacionImportacion?.incotermId ?? null,
                      });
                    }}
                    itemToLabel={(o) => o.folio}
                    renderTrigger={(o) => o.folio}
                    renderItem={(o) => o.folio}
                    initialLabel={ocLabel}
                    ariaLabel="Seleccionar OC de importación"
                    placeholder="Selecciona la OC"
                    emptyListText="Sin órdenes de importación disponibles."
                  />
                  <p className="text-xs text-ink-muted">
                    La sucursal, moneda e Incoterm se proponen desde la OC.
                  </p>
                </div>
              ) : (
                <>
                  <Label htmlFor="ce-clientes">Cliente(s) de la solicitud</Label>
                  <Input
                    id="ce-clientes"
                    required
                    value={datos.clientes.join('; ')}
                    onChange={(e) =>
                      update({
                        clientes: e.target.value
                          .split(';')
                          .map((s) => s.trim())
                          .filter(Boolean),
                      })
                    }
                    placeholder="Nombres separados por punto y coma"
                  />
                  <Label>Sucursal</Label>
                  <SucursalSelector
                    value={datos.sucursalId}
                    onChange={(id) => update({ sucursalId: id })}
                  />
                  <p className="text-xs text-ink-muted">
                    Incoterm propuesto: DDP. Los pedidos se vincularán en CE-02.
                  </p>
                </>
              )}
              <Label htmlFor="ce-ref">
                Referencia {datos.tipo === 2 ? 'de la solicitud de contenedor' : 'de la operación'}
              </Label>
              <Input
                id="ce-ref"
                value={datos.referencia}
                onChange={(e) => update({ referencia: e.target.value })}
                required
                maxLength={200}
              />
            </fieldset>
            <fieldset className="space-y-3">
              <legend className="text-sm font-semibold">2. Condiciones y responsable</legend>
              {canEditar && (
                <>
                  <Label>Incoterm</Label>
                  <IncotermSelector
                    value={datos.incotermId}
                    onChange={(id) => update({ incotermId: id })}
                    placeholder={datos.tipo === 2 ? 'DDP (propuesto)' : 'Incoterm de la OC'}
                  />
                </>
              )}
              <Label>Moneda ISO</Label>
              <MonedaSelector value={datos.moneda} onChange={(moneda) => update({ moneda })} />
              {usuarios ? (
                <>
                  <Label>Responsable</Label>
                  <UsuarioSelector
                    value={datos.responsableUsuarioId}
                    onChange={(id) => update({ responsableUsuarioId: id ?? '' })}
                  />
                </>
              ) : (
                <p className="text-sm">Responsable: {user?.nombre ?? '[RESPONSABLE]'}</p>
              )}
              <Label htmlFor="ce-tc">Tipo de cambio (opcional)</Label>
              <Input
                id="ce-tc"
                type="number"
                min="0.000001"
                step="0.000001"
                value={datos.tc ?? ''}
                onChange={(e) =>
                  update({
                    tc: e.target.value ? Number(e.target.value) : null,
                    ...(e.target.value ? {} : { fechaTc: null, fuenteTc: null }),
                  })
                }
              />
              {datos.tc !== null && (
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-1.5">
                    <Label htmlFor="ce-fecha-tc">Fecha del TC</Label>
                    <Input
                      id="ce-fecha-tc"
                      type="date"
                      required
                      value={datos.fechaTc ?? ''}
                      onChange={(e) => update({ fechaTc: e.target.value })}
                    />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="ce-fuente-tc">Fuente del TC</Label>
                    <Input
                      id="ce-fuente-tc"
                      required
                      maxLength={200}
                      value={datos.fuenteTc ?? ''}
                      onChange={(e) => update({ fuenteTc: e.target.value })}
                    />
                  </div>
                </div>
              )}
              <p className="text-xs text-ink-muted">
                Fuente oficial del TC: Por confirmar. Se conserva la fuente capturada.
              </p>
            </fieldset>
            {crear.isError && (
              <p role="alert" className="text-sm text-danger-fg">
                {esApiError(crear.error)
                  ? (crear.error.problem.detail ?? crear.error.problem.title)
                  : 'No se pudo guardar el expediente.'}
              </p>
            )}
          </div>
          <footer className="flex items-center justify-between border-t border-line p-4">
            <Button type="button" variant="ghost" onClick={cerrar}>
              Cancelar
            </Button>
            <Button
              type="submit"
              disabled={crear.isPending || bloqueo}
              title={
                bloqueo
                  ? 'Completa el origen de la operación.'
                  : crear.isPending
                    ? 'Guardando expediente.'
                    : undefined
              }
            >
              {crear.isPending ? 'Guardando…' : 'Crear expediente'}
            </Button>
          </footer>
        </form>
      </SheetContent>
    </Sheet>
  );
}
