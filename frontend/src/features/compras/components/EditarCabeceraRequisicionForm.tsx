import { Controller, useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { DatePickerField, Field, ProveedorSelector, TextAreaField } from '@/components/erp';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import {
  Clasificacion,
  Prioridad,
  clasificacionToString,
  prioridadToString,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import {
  useEditarCabeceraRequisicion,
  type EditarCabeceraRequisicionCommand,
} from '@/features/compras/api/useEditarCabeceraRequisicion';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';

interface Valores {
  clasificacion: Clasificacion;
  prioridad: Prioridad;
  fechaEntregaDeseada: string | null;
  proveedorSugeridoId: string | null;
  obra: string;
  soloApartarSiCompleto: boolean;
  descripcion: string | null;
}

/**
 * Edición de la cabecera de una RQ en Borrador (86e3tferc, D6) vía
 * <c>PATCH /api/v1/compras/requisiciones/{id}</c>. Mismos campos que la
 * pantalla de nueva requisición, salvo sucursal, departamento y requisitante,
 * que no cambian después de crearla. Solo envía lo que cambió.
 */
export function EditarCabeceraRequisicionForm({
  rq,
  onClose,
}: {
  rq: RequisicionResponse;
  onClose: () => void;
}) {
  const inicial: Valores = {
    clasificacion: rq.clasificacion,
    prioridad: rq.prioridad,
    fechaEntregaDeseada: rq.fechaEntregaDeseada ?? null,
    proveedorSugeridoId: rq.proveedorSugeridoId ?? null,
    obra: rq.obra ?? '',
    soloApartarSiCompleto: rq.soloApartarSiCompleto ?? false,
    descripcion: rq.descripcion ?? null,
  };
  const form = useForm<Valores>({ defaultValues: inicial });
  const idempotencyKey = useFormIdempotencyKey();
  const mutation = useEditarCabeceraRequisicion();

  function onSubmit(v: Valores) {
    const c: EditarCabeceraRequisicionCommand = {};
    if (v.clasificacion !== inicial.clasificacion) c.clasificacion = v.clasificacion;
    if (v.prioridad !== inicial.prioridad) c.prioridad = v.prioridad;
    if (v.fechaEntregaDeseada !== inicial.fechaEntregaDeseada) {
      if (v.fechaEntregaDeseada) c.fechaEntregaDeseada = v.fechaEntregaDeseada;
      else c.limpiarFechaEntregaDeseada = true;
    }
    if (v.proveedorSugeridoId !== inicial.proveedorSugeridoId) {
      if (v.proveedorSugeridoId) c.proveedorSugeridoId = v.proveedorSugeridoId;
      else c.limpiarProveedorSugeridoId = true;
    }
    const obra = v.obra.trim();
    if (obra !== inicial.obra) {
      if (obra) c.obra = obra;
      else c.limpiarObra = true;
    }
    if (v.soloApartarSiCompleto !== inicial.soloApartarSiCompleto)
      c.soloApartarSiCompleto = v.soloApartarSiCompleto;
    const descripcion = v.descripcion?.trim() ?? '';
    if (descripcion !== (inicial.descripcion ?? '')) {
      if (descripcion) c.descripcion = descripcion;
      else c.limpiarDescripcion = true;
    }
    if (Object.keys(c).length === 0) {
      onClose();
      return;
    }
    mutation.mutate(
      { requisicionId: rq.id, command: c, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Cabecera actualizada.');
          onClose();
        },
        onError: (err) => {
          if (
            esApiError(err) &&
            applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], err, {
              codigos: {
                PROVEEDOR_NO_ENCONTRADO: { campo: 'proveedorSugeridoId' },
                PROVEEDOR_NO_UTILIZABLE: { campo: 'proveedorSugeridoId' },
                PARTIDAS_NO_APLICAN_A_CLASIFICACION: { campo: 'clasificacion' },
              },
            }) === true
          )
            return;
          toast.error(
            esApiError(err)
              ? (err.problem.detail ?? err.problem.title)
              : 'Error inesperado al guardar la cabecera.',
          );
        },
      },
    );
  }

  const errores = form.formState.errors;
  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      aria-label="Editar cabecera"
      className="space-y-3 rounded-md border border-dashed border-warning bg-surface-muted/30 p-3"
    >
      <FormGrid>
        <FormCampo span={6}>
          <Field label="Clasificación" error={errores.clasificacion?.message} required>
            {(f) => (
              <Controller
                name="clasificacion"
                control={form.control}
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(v) => field.onChange(Number(v) as Clasificacion)}
                  >
                    <SelectTrigger {...f}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(
                        [
                          Clasificacion.Servicio,
                          Clasificacion.OrdenCompra,
                          Clasificacion.MateriaPrima,
                          Clasificacion.Pinturas,
                        ] as const
                      ).map((c) => (
                        <SelectItem key={c} value={String(c)}>
                          {clasificacionToString(c)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            )}
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Prioridad" error={errores.prioridad?.message} required>
            {(f) => (
              <Controller
                name="prioridad"
                control={form.control}
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(v) => field.onChange(Number(v) as Prioridad)}
                  >
                    <SelectTrigger {...f}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {([Prioridad.Baja, Prioridad.Normal, Prioridad.Alta] as const).map((p) => (
                        <SelectItem key={p} value={String(p)}>
                          {prioridadToString(p)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            )}
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Fecha entrega deseada" error={errores.fechaEntregaDeseada?.message}>
            {(f) => (
              <Controller
                name="fechaEntregaDeseada"
                control={form.control}
                render={({ field }) => (
                  <DatePickerField
                    id={f.id}
                    value={field.value}
                    onChange={(d) => field.onChange(d)}
                    minDate={new Date()}
                  />
                )}
              />
            )}
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Proveedor sugerido" error={errores.proveedorSugeridoId?.message}>
            {(f) => (
              <Controller
                name="proveedorSugeridoId"
                control={form.control}
                render={({ field }) => (
                  <ProveedorSelector
                    triggerProps={f}
                    value={field.value}
                    onChange={(id) => field.onChange(id ?? null)}
                    initialLabel={rq.proveedorSugeridoRazonSocial ?? undefined}
                  />
                )}
              />
            )}
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Obra (opcional)" error={errores.obra?.message}>
            {(f) => (
              <Input
                {...f}
                {...form.register('obra', { maxLength: { value: 120, message: 'Máximo 120 caracteres' } })}
                maxLength={120}
                placeholder="Nombre o referencia de la obra"
              />
            )}
          </Field>
        </FormCampo>
        <FormCampo span={12}>
          <Controller
            name="soloApartarSiCompleto"
            control={form.control}
            render={({ field }) => (
              <fieldset className="space-y-2">
                <legend className="text-sm font-medium text-ink">Surtido de almacén</legend>
                <div role="radiogroup" className="grid gap-2 sm:grid-cols-2">
                  {[
                    { v: false, t: 'Parcial: aparta lo que haya y compra el resto' },
                    { v: true, t: 'Completo: si no alcanza toda la línea, se compra completa' },
                  ].map((o) => (
                    <label key={String(o.v)} className="flex items-start gap-2 text-sm text-ink">
                      <input
                        type="radio"
                        name="editar-surtido-almacen"
                        className="mt-0.5 accent-primary"
                        checked={field.value === o.v}
                        onChange={() => field.onChange(o.v)}
                      />
                      {o.t}
                    </label>
                  ))}
                </div>
              </fieldset>
            )}
          />
        </FormCampo>
        <FormCampo span={12}>
          <Field label="Descripción" error={errores.descripcion?.message}>
            {(f) => (
              <Controller
                name="descripcion"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    textareaProps={f}
                    value={field.value}
                    onChange={(v) => field.onChange(v)}
                    maxLength={500}
                    minRows={3}
                  />
                )}
              />
            )}
          </Field>
        </FormCampo>
      </FormGrid>
      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onClose} disabled={mutation.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? 'Guardando…' : 'Guardar cabecera'}
        </Button>
      </div>
    </form>
  );
}
