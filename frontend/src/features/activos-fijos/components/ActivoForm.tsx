import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { EmpleadoSelector } from '@/components/erp/selectors/EmpleadoSelector';
import { Dim3Picker } from '@/features/centros-costo/components/Dim3Picker';
import { useState } from 'react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetBody,
  SheetFooter,
  SheetDescription,
} from '@/components/ui/sheet';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useClasesActivo, useGuardarActivo } from '../api/hooks';
import { ActivoMejoradoSelector } from './ActivoMejoradoSelector';
import type { Activo, Custodia, DatosActivo } from '../api/types';
const vacio = '00000000-0000-0000-0000-000000000000';
export function CustodiaFields({
  value,
  onChange,
}: {
  value: Custodia;
  onChange: (c: Custodia) => void;
}) {
  const [empleado, setEmpleado] = useState(false);
  return (
    <fieldset className="space-y-3">
      <legend className="text-sm font-semibold">1. Custodia</legend>
      <FormGrid>
        <FormCampo>
          <label className="space-y-1 text-xs">
            Sucursal *
            <SucursalSelector
              value={value.sucursalId || null}
              onChange={(id) => onChange({ ...value, sucursalId: id ?? '' })}
            />
          </label>
        </FormCampo>
        <FormCampo>
          <label className="space-y-1 text-xs">
            Responsable{' '}
            <input
              type="checkbox"
              aria-label="Responsable es empleado"
              checked={empleado}
              onChange={(e) => {
                setEmpleado(e.target.checked);
                onChange({ ...value, responsableId: vacio });
              }}
            />{' '}
            Empleado
            {empleado ? (
              <EmpleadoSelector
                value={value.responsableId === vacio ? null : value.responsableId}
                onChange={(id) => onChange({ ...value, responsableId: id ?? vacio })}
              />
            ) : (
              <UsuarioSelector
                value={value.responsableId === vacio ? null : value.responsableId}
                onChange={(id) => onChange({ ...value, responsableId: id ?? vacio })}
              />
            )}
          </label>
        </FormCampo>
        <FormCampo>
          <label className="space-y-1 text-xs">
            Ubicación física
            <Input
              value={value.ubicacion}
              onChange={(e) => onChange({ ...value, ubicacion: e.target.value })}
            />
          </label>
        </FormCampo>
        <FormCampo>
          <label className="space-y-1 text-xs">
            Centro de costo
            <Dim3Picker
              endpoint="/api/v1/activos-fijos/centros"
              value={value.centroCostoId === vacio ? null : value.centroCostoId}
              onChange={(id) => onChange({ ...value, centroCostoId: id ?? vacio })}
              ariaLabel="Centro de costo"
            />
          </label>
        </FormCampo>
        <FormCampo>
          <label className="space-y-1 text-xs">
            Equipo (opcional)
            <Dim3Picker
              endpoint="/api/v1/activos-fijos/equipos"
              value={value.equipoId}
              onChange={(id) => onChange({ ...value, equipoId: id })}
              ariaLabel="Equipo"
            />
          </label>
        </FormCampo>
      </FormGrid>
    </fieldset>
  );
}
export function ActivoForm({ activo, onClose }: { activo?: Activo; onClose: () => void }) {
  const sucursal = useAuthStore((s) => s.currentSucursalId);
  const clases = useClasesActivo();
  const guardar = useGuardarActivo();
  const [dirty, setDirty] = useState(false);
  const [d, setD] = useState<DatosActivo>(() =>
    activo
      ? {
          ...activo,
          custodia: {
            responsableId: activo.responsableId,
            sucursalId: activo.sucursalId,
            ubicacion: activo.ubicacion,
            centroCostoId: activo.centroCostoId,
            equipoId: activo.equipoId,
          },
        }
      : {
          descripcion: '',
          claseId: '',
          marca: null,
          modelo: null,
          numeroSerie: null,
          fechaAdquisicion: '',
          inicioUso: null,
          valorMonedaOriginal: 0,
          moneda: 'MXN',
          tipoCambio: 1,
          custodia: {
            responsableId: vacio,
            sucursalId: sucursal ?? '',
            ubicacion: '',
            centroCostoId: vacio,
            equipoId: null,
          },
          importacion: 0,
          pedimento: null,
          arrendado: false,
          documentoSoporte: null,
        },
  );
  const set = (patch: Partial<DatosActivo>) => {
    setDirty(true);
    setD({ ...d, ...patch });
  };
  function cerrar() {
    if (!dirty || window.confirm('¿Descartar los cambios del activo?')) onClose();
  }
  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) cerrar();
      }}
    >
      <SheetContent size="lg">
        <SheetHeader>
          <SheetTitle>{activo ? 'Completar activo' : 'Alta manual de activo'}</SheetTitle>
          <SheetDescription>
            Complete los datos y conserve el documento de soporte. La capitalización se realiza
            después.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          <form
            id="activo-form"
            className="space-y-5 text-sm"
            onSubmit={(e) => {
              e.preventDefault();
              guardar.mutate(
                {
                  ruta: activo ? `activos/${activo.id}/editar` : 'activos',
                  body: { datos: d },
                  version: activo?.version,
                },
                { onSuccess: onClose },
              );
            }}
          >
            <fieldset className="grid grid-cols-2 gap-4">
              <legend className="mb-3 font-semibold">Identificación y valores</legend>
              <label className="col-span-2 space-y-1 text-xs">
                Descripción *
                <Input
                  required
                  maxLength={500}
                  value={d.descripcion}
                  onChange={(e) => set({ descripcion: e.target.value })}
                />
              </label>
              <label className="space-y-1 text-xs">
                Clase *
                <select
                  required
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                  value={d.claseId}
                  onChange={(e) =>
                    set({ claseId: e.target.value, activoMejoradoId: null, vidaUtilMeses: null })
                  }
                >
                  <option value="">Seleccione clase</option>
                  {clases.data
                    ?.filter((c) => c.activa)
                    .map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.clave} · {c.nombre}
                      </option>
                    ))}
                </select>
              </label>
              {clases.data?.find((c) => c.id === d.claseId)?.clave === '170' && (
                <label className="space-y-1 text-xs">
                  Activo que mejora
                  <ActivoMejoradoSelector
                    value={d.activoMejoradoId ?? null}
                    excluirId={activo?.id}
                    onChange={(id) => set({ activoMejoradoId: id })}
                  />
                  <span>Sin vínculo no se calcula depreciación.</span>
                </label>
              )}
              {clases.data?.find((c) => c.id === d.claseId)?.clave === '160' && (
                <label className="space-y-1 text-xs">
                  Vida útil en meses
                  <Input
                    type="number"
                    min="1"
                    max="600"
                    step="1"
                    value={d.vidaUtilMeses ?? ''}
                    onChange={(e) =>
                      set({ vidaUtilMeses: e.target.value === '' ? null : Number(e.target.value) })
                    }
                  />
                  <span>De 1 a 600 meses. Sin vida útil no se calcula depreciación.</span>
                </label>
              )}
              {(['marca', 'modelo', 'numeroSerie', 'documentoSoporte', 'pedimento'] as const).map(
                (k, i) => (
                  <label key={k} className="space-y-1 text-xs">
                    {
                      ['Marca', 'Modelo', 'Número de serie', 'Documento de soporte *', 'Pedimento'][
                        i
                      ]
                    }
                    <Input
                      required={
                        (k === 'documentoSoporte' && !activo?.ordenCompraId) ||
                        (k === 'pedimento' && d.importacion !== 0)
                      }
                      value={d[k] ?? ''}
                      onChange={(e) => set({ [k]: e.target.value || null })}
                    />
                  </label>
                ),
              )}
              <label className="space-y-1 text-xs">
                Adquisición *
                <Input
                  required
                  type="date"
                  value={d.fechaAdquisicion}
                  onChange={(e) => set({ fechaAdquisicion: e.target.value })}
                />
              </label>
              <label className="space-y-1 text-xs">
                Inicio de uso
                <Input
                  type="date"
                  min={d.fechaAdquisicion}
                  value={d.inicioUso ?? ''}
                  onChange={(e) => set({ inicioUso: e.target.value || null })}
                />
              </label>
              <label className="space-y-1 text-xs">
                Valor en moneda original *
                <Input
                  required
                  type="number"
                  min="0"
                  step="0.01"
                  readOnly={!!activo?.facturaProveedorId}
                  value={d.valorMonedaOriginal}
                  onChange={(e) => set({ valorMonedaOriginal: Number(e.target.value) })}
                />
              </label>
              <label className="space-y-1 text-xs">
                Moneda *
                <Input
                  required
                  maxLength={3}
                  readOnly={!!activo?.facturaProveedorId}
                  value={d.moneda}
                  onChange={(e) => set({ moneda: e.target.value.toUpperCase() })}
                />
              </label>
              <label className="space-y-1 text-xs">
                Tipo de cambio *
                <Input
                  required
                  type="number"
                  min="0.000001"
                  step="0.000001"
                  readOnly={!!activo?.facturaProveedorId}
                  value={d.tipoCambio}
                  onChange={(e) => set({ tipoCambio: Number(e.target.value) })}
                />
              </label>
              <label className="space-y-1 text-xs">
                Importación
                <select
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                  value={d.importacion}
                  onChange={(e) => set({ importacion: Number(e.target.value) })}
                >
                  <option value={0}>No</option>
                  <option value={1}>Temporal</option>
                  <option value={2}>Definitiva</option>
                </select>
              </label>
              <label className="flex items-center gap-2 text-xs">
                <input
                  type="checkbox"
                  checked={d.arrendado}
                  onChange={(e) => set({ arrendado: e.target.checked })}
                />
                Arrendado
              </label>
            </fieldset>
            <CustodiaFields value={d.custodia} onChange={(custodia) => set({ custodia })} />
            {guardar.error && (
              <p role="alert" className="text-danger-fg">
                {guardar.error.message}
              </p>
            )}
          </form>
        </SheetBody>
        <SheetFooter>
          <Button variant="outline" onClick={cerrar}>
            Cancelar
          </Button>
          <Button
            form="activo-form"
            type="submit"
            disabled={guardar.isPending}
            title={guardar.isPending ? 'Guardando el activo' : undefined}
          >
            Guardar
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
