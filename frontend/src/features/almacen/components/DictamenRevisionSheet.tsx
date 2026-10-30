import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { SheetBody } from '@/components/ui/sheet';
import { useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { toast } from 'sonner';
import { useNavigate } from '@tanstack/react-router';
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
import { areaHojaDe, textoMedida } from '@/features/almacen/lib/medida-hoja';
import { useDictaminarMaterialEnRevision } from '@/features/almacen/api/useMaterialEnRevision';
import {
  ResultadoDictamen,
  ResultadoDictamenLabels,
  type MaterialEnRevisionItem,
} from '@/features/almacen/api/types';
import { Field } from '@/features/almacen/components/internal/Field';

const DESCRIPCION_RESULTADO: Partial<Record<ResultadoDictamen, string>> = {
  [ResultadoDictamen.Rechazar]:
    'Abre una devolución al proveedor con la evidencia de la recepción; la autoriza Dirección.',
};

const MENSAJE_RACK = 'Elige la ubicación (rack) disponible a la que se libera el material.';
const MENSAJE_GENERAL =
  'Elige la ubicación (rack o almacén general) a la que se libera el material.';

function crearSchema(pendiente: number, esVidrio: boolean) {
  return z
    .object({
      resultado: z.union([
        z.literal(ResultadoDictamen.Liberar),
        z.literal(ResultadoDictamen.Rechazar),
        z.literal(ResultadoDictamen.Baja),
      ]),
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
        ctx.addIssue({ code: 'custom', path: ['ubicacionDestinoId'],
          message: esVidrio ? MENSAJE_RACK : MENSAJE_GENERAL,
        });
      }
    });
}
type FormValues = z.infer<ReturnType<typeof crearSchema>>;

/** « (2140 × 3300 mm · 6 hojas)» para el vidrio por medida; vacío si la línea no tiene medida. */
function conMedida(item: MaterialEnRevisionItem, cantidad: number): string {
  const t = textoMedida({ ...item, areaHojaM2: areaHojaDe(item.anchoMm, item.altoMm) }, cantidad);
  return t ? ` (${t})` : '';
}

export interface DictamenRevisionSheetProps {
  item: MaterialEnRevisionItem | null;
  onOpenChange: (open: boolean) => void;
  /** Abre el panel de evidencia de la fila (se ofrece cuando el rechazo se bloquea por falta de evidencia). */
  onAgregarEvidencia?: (item: MaterialEnRevisionItem) => void;
}

/**
 * ALM-04 G1.3 — Dictamen del material en revisión (liberar, dar de baja o rechazar al proveedor). Libera y baja mueven
 * inventario y no se deshacen; el rechazo abre una devolución que autoriza Dirección. Todo pide confirmación.
 */
export function DictamenRevisionSheet({
  item,
  onOpenChange,
  onAgregarEvidencia,
}: DictamenRevisionSheetProps) {
  return (
    <Sheet open={item !== null} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Dictaminar material en revisión</SheetTitle>
          <SheetDescription>
            {item
              ? `Recepción ${item.recepcionFolio} · ${item.articuloClave ?? ''} · pendiente ${item.cantidadPendiente} ${item.unidadMedida}${conMedida(item, item.cantidadPendiente)}`
              : ''}
          </SheetDescription>
        </SheetHeader>
        {item && (
          <Cuerpo
            key={item.lineaRevisionId}
            item={item}
            onClose={() => onOpenChange(false)}
            onAgregarEvidencia={onAgregarEvidencia}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function Cuerpo({
  item,
  onClose,
  onAgregarEvidencia,
}: {
  item: MaterialEnRevisionItem;
  onClose: () => void;
  onAgregarEvidencia?: (item: MaterialEnRevisionItem) => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const dictaminar = useDictaminarMaterialEnRevision();
  const [confirmar, setConfirmar] = useState(false);
  const [errorServidor, setErrorServidor] = useState<string | null>(null);
  const [sinEvidencia, setSinEvidencia] = useState<string | null>(null);

  const form = useForm<FormValues>({
    resolver: zodResolver(crearSchema(item.cantidadPendiente, !!item.productoAwVarianteId)),
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
  const verbo =
    resultado === ResultadoDictamen.Liberar
      ? 'Liberar'
      : resultado === ResultadoDictamen.Rechazar
        ? 'Rechazar al proveedor'
        : 'Dar de baja';

  function enviar() {
    const v = form.getValues();
    setErrorServidor(null);
    setSinEvidencia(null);
    dictaminar.mutate(
      {
        lineaRevisionId: item.lineaRevisionId,
        idempotencyKey,
        command: {
          resultado: v.resultado,
          cantidad: v.cantidad,
          motivo: v.motivo,
          fechaMovimiento: v.fechaMovimiento,
          ubicacionDestinoId:
            v.resultado === ResultadoDictamen.Liberar ? v.ubicacionDestinoId : null,
        },
      },
      {
        onSuccess: (resp) => {
          const devolucionId = resp.devolucionAProveedorId;
          if (devolucionId) {
            toast.success('Devolución abierta para autorización de Dirección', {
              action: {
                label: 'Ver devolución',
                onClick: () =>
                  void navigate({
                    to: '/almacen/devoluciones/proveedor/$id',
                    params: { id: devolucionId },
                  }),
              },
            });
          } else {
            toast.success(`Dictamen registrado${resp.folio ? ` · ${resp.folio}` : ''}`);
          }
          setConfirmar(false);
          onClose();
        },
        onError: (error) => {
          setConfirmar(false);
          if (esApiError(error)) {
            if (error.problem.code === 'DICTAMEN_RECHAZO_SIN_EVIDENCIA') {
              setSinEvidencia(error.problem.detail ?? error.problem.title);
              return;
            }
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            )
              return;
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
      <SheetBody>
        <form
          id="dictamen-form"
          noValidate
          onSubmit={form.handleSubmit(() => setConfirmar(true))}
          className="space-y-4"
        >
          {errorServidor && <AvisoEnLinea tono="error">{errorServidor}</AvisoEnLinea>}
          {sinEvidencia && (
            <AvisoEnLinea
              tono="advertencia"
              accion={
                onAgregarEvidencia && (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => onAgregarEvidencia(item)}
                  >
                    Agregar evidencia
                  </Button>
                )
              }
            >
              {sinEvidencia}
            </AvisoEnLinea>
          )}

          <fieldset className="space-y-1">
            <legend className="text-sm font-medium text-ink">Resultado del dictamen</legend>
            <Controller
              name="resultado"
              control={form.control}
              render={({ field }) => (
                <div className="flex flex-col gap-2">
                  {(
                    [
                      ResultadoDictamen.Liberar,
                      ResultadoDictamen.Rechazar,
                      ResultadoDictamen.Baja,
                    ] as const
                  ).map((r) => (
                    <label key={r} className="flex items-start gap-2 text-sm">
                      <input
                        type="radio"
                        name="resultado-dictamen"
                        className="mt-1"
                        checked={field.value === r}
                        onChange={() => field.onChange(r)}
                      />
                      <span>
                        {ResultadoDictamenLabels[r]}
                        {DESCRIPCION_RESULTADO[r] && (
                          <span className="block text-xs text-ink-muted">
                            {DESCRIPCION_RESULTADO[r]}
                          </span>
                        )}
                      </span>
                    </label>
                  ))}
                </div>
              )}
            />
          </fieldset>

          <FormSeccion numero={1} titulo="Datos del dictamen">
            <FormCampo span={6}>
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
                      onChange={(e) =>
                        field.onChange(e.target.value === '' ? '' : Number(e.target.value))
                      }
                    />
                  )}
                />
              </Field>
            </FormCampo>
            <FormCampo span={6}>
              <Field label="Fecha de movimiento" required error={errors.fechaMovimiento?.message}>
                <Controller
                  name="fechaMovimiento"
                  control={form.control}
                  render={({ field }) => <Input {...field} type="date" />}
                />
              </Field>
            </FormCampo>
          </FormSeccion>

          {resultado === ResultadoDictamen.Liberar && (
            <Field
              label="Ubicación (rack) disponible"
              required
              error={errors.ubicacionDestinoId?.message}
            >
              <Controller
                name="ubicacionDestinoId"
                control={form.control}
                render={({ field }) => (
                  <UbicacionBinSelector
                    modo="asignacion"
                    articuloId={item.articuloId}
                    subAlmacenId={null}
                    requiereSubAlmacen={false}
                    incluirGeneral={!item.productoAwVarianteId}
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
      </SheetBody>

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
            {verbo} {form.getValues('cantidad')} {item.unidadMedida} de{' '}
            {item.articuloClave ?? 'este artículo'}
            {conMedida(item, Number(form.getValues('cantidad')))}.{' '}
            {resultado === ResultadoDictamen.Rechazar
              ? 'Se abre una devolución al proveedor para autorización de Dirección; esta acción no se puede deshacer.'
              : 'Esta acción mueve inventario y no se puede deshacer.'}
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
