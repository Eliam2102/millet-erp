import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { useState, type FormEvent } from 'react';
import { useNavigate } from '@tanstack/react-router';
import {
  Sheet,
  SheetContent,
  SheetBody,
  SheetFooter,
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
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(onClose);
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
    if (!crear.isPending) solicitarCierre();
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
      <SheetContent size="lg">
        <SheetHeader>
          <SheetTitle>Nuevo expediente</SheetTitle>
          <SheetDescription>Captura el origen y los datos de la operación.</SheetDescription>
        </SheetHeader>
        <form onSubmit={submit} className="flex min-h-0 flex-1 flex-col">
          <SheetBody className="space-y-5">
            <FormSeccion titulo="1. Origen de la operación">
              <FormCampo span={6} className="space-y-1.5">
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
              </FormCampo>
              <FormCampo span={12}>
                {datos.tipo === 1 ? (
                  <FormGrid className="col-span-12">
                    <FormCampo span={6} className="space-y-1.5">
                      <Label htmlFor="ce-buscar-oc">Buscar OC por folio</Label>
                      <Input
                        id="ce-buscar-oc"
                        value={busquedaOc}
                        onChange={(e) => setBusquedaOc(e.target.value)}
                        placeholder="Folio de la OC"
                      />
                    </FormCampo>
                    <FormCampo span={6} className="space-y-1.5">
                      <Label>
                        Orden de compra con importación <span aria-hidden="true">*</span>
                      </Label>
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
                    </FormCampo>
                    <p className="col-span-12 text-xs text-ink-muted">
                      La sucursal, moneda e Incoterm se proponen desde la OC.
                    </p>
                  </FormGrid>
                ) : (
                  <FormGrid className="col-span-12">
                    <FormCampo span={6} className="space-y-1.5">
                      <Label htmlFor="ce-clientes">
                        Cliente(s) de la solicitud <span aria-hidden="true">*</span>
                      </Label>
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
                    </FormCampo>
                    <FormCampo span={6} className="space-y-1.5">
                      <Label>
                        Sucursal <span aria-hidden="true">*</span>
                      </Label>
                      <SucursalSelector
                        value={datos.sucursalId}
                        onChange={(id) => update({ sucursalId: id })}
                      />
                    </FormCampo>
                    <p className="col-span-12 text-xs text-ink-muted">
                      Incoterm propuesto: DDP. Vincule los clientes y asigne los pedidos en la
                      pestaña Contenedores.
                    </p>
                  </FormGrid>
                )}
              </FormCampo>
              <FormCampo span={6} className="space-y-1.5">
                <Label htmlFor="ce-ref">
                  Referencia{' '}
                  {datos.tipo === 2 ? 'de la solicitud de contenedor' : 'de la operación'}
                  <span aria-hidden="true">*</span>
                </Label>
                <Input
                  id="ce-ref"
                  value={datos.referencia}
                  onChange={(e) => update({ referencia: e.target.value })}
                  required
                  maxLength={200}
                />
              </FormCampo>
            </FormSeccion>
            <FormSeccion titulo="2. Condiciones y responsable">
              {canEditar && (
                <>
                  <FormCampo span={6} className="space-y-1.5">
                    <Label>Incoterm</Label>
                    <IncotermSelector
                      value={datos.incotermId}
                      onChange={(id) => update({ incotermId: id })}
                      placeholder={datos.tipo === 2 ? 'DDP (propuesto)' : 'Incoterm de la OC'}
                    />
                  </FormCampo>
                </>
              )}
              <FormCampo span={6} className="space-y-1.5">
                <Label>Moneda ISO {datos.tipo === 2 && <span aria-hidden="true">*</span>}</Label>
                <MonedaSelector value={datos.moneda} onChange={(moneda) => update({ moneda })} />
              </FormCampo>
              <FormCampo span={12}>
                {usuarios ? (
                  <>
                    <FormCampo span={6} className="space-y-1.5">
                      <Label>
                        Responsable <span aria-hidden="true">*</span>
                      </Label>
                      <UsuarioSelector
                        value={datos.responsableUsuarioId}
                        onChange={(id) => update({ responsableUsuarioId: id ?? '' })}
                      />
                    </FormCampo>
                  </>
                ) : (
                  <p className="text-sm">Responsable: {user?.nombre ?? 'Sin asignar'}</p>
                )}
              </FormCampo>
              <FormCampo span={6} className="space-y-1.5">
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
              </FormCampo>
              {datos.tc !== null && (
                <FormGrid className="col-span-12">
                  <FormCampo span={6} className="space-y-1.5">
                    <Label htmlFor="ce-fecha-tc">
                      Fecha del TC <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id="ce-fecha-tc"
                      type="date"
                      required
                      value={datos.fechaTc ?? ''}
                      onChange={(e) => update({ fechaTc: e.target.value })}
                    />
                  </FormCampo>
                  <FormCampo span={6} className="space-y-1.5">
                    <Label htmlFor="ce-fuente-tc">
                      Fuente del TC <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id="ce-fuente-tc"
                      required
                      maxLength={200}
                      value={datos.fuenteTc ?? ''}
                      onChange={(e) => update({ fuenteTc: e.target.value })}
                    />
                  </FormCampo>
                </FormGrid>
              )}
              <FormCampo span={12}>
                <p className="text-xs text-ink-muted">
                  Se conserva la fuente del tipo de cambio capturada en la operación.
                </p>
              </FormCampo>
            </FormSeccion>
            {crear.isError && (
              <p role="alert" className="text-sm text-danger-fg">
                {esApiError(crear.error)
                  ? (crear.error.problem.detail ?? crear.error.problem.title)
                  : 'No se pudo guardar el expediente.'}
              </p>
            )}
          </SheetBody>
          <SheetFooter>
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
          </SheetFooter>
        </form>
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}
