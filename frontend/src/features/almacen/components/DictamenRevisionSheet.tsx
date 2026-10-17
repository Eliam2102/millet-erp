import { useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { TextAreaField } from '@/components/erp';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { useDictaminarMaterialEnRevision } from '@/features/almacen/api/useMaterialEnRevision';
import {
  ResultadoDictamen,
  ResultadoDictamenLabels,
  type MaterialEnRevisionItem,
} from '@/features/almacen/api/types';
import { Field } from '@/features/almacen/components/internal/Field';

const MENSAJE_RACK = 'Elige la ubicación (rack) disponible a la que se libera el material.';

function crearSchema(pendiente: number) {
  return z
    .object({
      resultado: z.union([z.literal(ResultadoDictamen.Liberar), z.literal(ResultadoDictamen.Baja)]),
      cantidad: z
        .number({ message: 'Cantidad requerida' })
        .positive('La cantidad debe ser mayor a cero')
        .max(pendiente, `No puedes dictaminar más de lo pendiente (${pendiente}).`),
      motivo: z.string().trim().min(1, 'Indica el motivo del dictamen.').max(500),
      fechaMovimiento: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Fecha en formato YYYY-MM-DD'),
      ubicacionDestinoId: z.string().nullable(),
    })
    .superRefine((v, ctx) => {
      if (v.resultado === ResultadoDictamen.Liberar && !v.ubicacionDestinoId) {
        ctx.addIssue({ code: 'custom', path: ['ubicacionDestinoId'], message: MENSAJE_RACK });
      }
    });
}
type FormValues = z.infer<ReturnType<typeof crearSchema>>;

export interface DictamenRevisionSheetProps {
  item: MaterialEnRevisionItem | null;
  onOpenChange: (open: boolean) => void;
}

/**
 * ALM-04 G1.3 — Dictamen del material en revisión (liberar o dar de baja). Mueve inventario y no se
 * deshace, por eso pide confirmación antes de enviar. «Rechazar al proveedor» llega en la rebanada 4.
 */
export function DictamenRevisionSheet({ item, onOpenChange }: DictamenRevisionSheetProps) {
  return (
    <Sheet open={item !== null} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="form">
        <SheetHeader>
          <SheetTitle>Dictaminar material en revisión</SheetTitle>
          <SheetDescription>
            {item ? `Recepción ${item.recepcionFolio} · ${item.articuloClave ?? ''} · pendiente ${item.cantidadPendiente} ${item.unidadMedida}` : ''}
          </SheetDescription>
        </SheetHeader>
        {item && <Cuerpo key={item.lineaRevisionId} item={item} onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Cuerpo({ item, onClose }: { item: MaterialEnRevisionItem; onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const dictaminar = useDictaminarMaterialEnRevision();
  const [confirmar, setConfirmar] = useState(false);
  const [errorServidor, setErrorServidor] = useState<string | null>(null);

  const form = useForm<FormValues>({
    resolver: zodResolver(crearSchema(item.cantidadPendiente)),
    defaultValues: {
      resultado: ResultadoDictamen.Liberar,
      cantidad: item.cantidadPendiente,
      motivo: '',
      fechaMovimiento: hoyLocalISO(),
      ubicacionDestinoId: item.ubicacionDisponibleSugeridaId,
    },
  });
  const resultado = useWatch({ control: form.control, name: 'resultado' });
  const errors = form.formState.errors;
  const verbo = resultado === ResultadoDictamen.Liberar ? 'Liberar' : 'Dar de baja';

  function enviar() {
    const v = form.getValues();
    setErrorServidor(null);
    dictaminar.mutate(
      {
        lineaRevisionId: item.lineaRevisionId,
        idempotencyKey,
        command: {
          resultado: v.resultado,
          cantidad: v.cantidad,
          motivo: v.motivo,
          fechaMovimiento: v.fechaMovimiento,
          ubicacionDestinoId: v.resultado === ResultadoDictamen.Liberar ? v.ubicacionDestinoId : null,
        },
      },
      {
        onSuccess: (resp) => {
          toast.success(`Dictamen registrado${resp.folio ? ` · ${resp.folio}` : ''}`);
          setConfirmar(false);
          onClose();
        },
        onError: (error) => {
          setConfirmar(false);
          if (esApiError(error)) {
            if (applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)) return;
            setErrorServidor(error.problem.detail ?? error.problem.title);
            return;
          }
          setErrorServidor('Error inesperado.');
        },
      },
    );
  }

  return (
    <>
      <form
        id="dictamen-form"
        noValidate
        onSubmit={form.handleSubmit(() => setConfirmar(true))}
        className="flex-1 space-y-4 overflow-y-auto px-6"
      >
        {errorServidor && <AvisoEnLinea tono="error">{errorServidor}</AvisoEnLinea>}

        <fieldset className="space-y-1">
          <legend className="text-sm font-medium text-ink">Resultado del dictamen</legend>
          <Controller
            name="resultado"
            control={form.control}
            render={({ field }) => (
              <div className="flex gap-4">
                {([ResultadoDictamen.Liberar, ResultadoDictamen.Baja] as const).map((r) => (
                  <label key={r} className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="resultado-dictamen"
                      checked={field.value === r}
                      onChange={() => field.onChange(r)}
                    />
                    {ResultadoDictamenLabels[r]}
                  </label>
                ))}
              </div>
            )}
          />
        </fieldset>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field label="Cantidad" required error={errors.cantidad?.message}>
            <Controller
              name="cantidad"
              control={form.control}
              render={({ field }) => (
                <Input
                  type="number"
                  inputMode="decimal"
                  min="0"
                  step="any"
                  value={field.value ?? ''}
                  onChange={(e) => field.onChange(e.target.value === '' ? '' : Number(e.target.value))}
                />
              )}
            />
          </Field>
          <Field label="Fecha de movimiento" required error={errors.fechaMovimiento?.message}>
            <Controller
              name="fechaMovimiento"
              control={form.control}
              render={({ field }) => <Input {...field} type="date" />}
            />
          </Field>
        </div>

        {resultado === ResultadoDictamen.Liberar && (
          <Field label="Ubicación (rack) disponible" required error={errors.ubicacionDestinoId?.message}>
            <Controller
              name="ubicacionDestinoId"
              control={form.control}
              render={({ field }) => (
                <UbicacionBinSelector
                  modo="asignacion"
                  articuloId={item.articuloId}
                  subAlmacenId={null}
                  requiereSubAlmacen={false}
                  value={field.value}
                  onChange={(id) => field.onChange(id)}
                />
              )}
            />
          </Field>
        )}

        <Field label="Motivo" required error={errors.motivo?.message}>
          <Controller
            name="motivo"
            control={form.control}
            render={({ field }) => (
              <TextAreaField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v ?? '')}
                maxLength={500}
                minRows={2}
              />
            )}
          />
        </Field>
      </form>

      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onClose} disabled={dictaminar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="dictamen-form" disabled={dictaminar.isPending}>
          {verbo}
        </Button>
      </SheetFooter>

      <ConfirmarAccionDialog
        open={confirmar}
        onOpenChange={setConfirmar}
        titulo={`${verbo} material en revisión`}
        resumen={
          <p>
            {verbo} {form.getValues('cantidad')} {item.unidadMedida} de {item.articuloClave ?? 'este artículo'}. Esta
            acción mueve inventario y no se puede deshacer.
          </p>
        }
        verbo={verbo}
        tono={resultado === ResultadoDictamen.Baja ? 'peligro' : 'primario'}
        onConfirmar={enviar}
        pendiente={dictaminar.isPending}
      />
    </>
  );
}
