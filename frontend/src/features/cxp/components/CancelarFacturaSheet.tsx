import { useEffect } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
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
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CancelarFacturaSchema,
  type CancelarFacturaValues,
} from '@/features/cxp/schemas/factura';
import { useCancelarFactura } from '@/features/cxp/api/useFacturas';
import {
  MotivoCancelacion,
  MotivoCancelacionLabels,
  type FacturaDetalle,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;CancelarFacturaSheet/&gt;</c> — slide-from-right para cancelar
 * una factura en estado <c>Capturada</c> o <c>EnRevision</c>. Selecciona
 * motivo enumerado + texto opcional (obligatorio si motivo = "Otro").
 */
export interface CancelarFacturaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  factura: FacturaDetalle | null;
}

const MOTIVOS_DISPONIBLES: readonly MotivoCancelacion[] = [
  MotivoCancelacion.RechazadaPorTolerancia,
  MotivoCancelacion.CfdiCanceladoEnSat,
  MotivoCancelacion.ErrorCaptura,
  MotivoCancelacion.OtroConTexto,
];

export function CancelarFacturaSheet({
  open,
  onOpenChange,
  factura,
}: CancelarFacturaSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const cancelar = useCancelarFactura();

  const form = useForm<CancelarFacturaValues>({
    resolver: zodResolver(CancelarFacturaSchema),
    defaultValues: {
      motivo: MotivoCancelacion.ErrorCaptura,
      texto: null,
    },
  });

  useEffect(() => {
    if (open) {
      form.reset({
        motivo: MotivoCancelacion.ErrorCaptura,
        texto: null,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, factura?.id]);

  const motivoSeleccionado = useWatch({
    control: form.control,
    name: 'motivo',
  });
  const requiereTexto = motivoSeleccionado === MotivoCancelacion.OtroConTexto;

  function onSubmit(values: CancelarFacturaValues) {
    if (!factura) return;
    cancelar.mutate(
      {
        id: factura.id,
        versionEsperada: factura.version,
        command: {
          motivo: values.motivo,
          texto: values.texto ?? null,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Factura cancelada');
          onOpenChange(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al cancelar la factura.');
        },
      },
    );
  }

  const textoError = form.formState.errors.texto;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Cancelar factura</SheetTitle>
          <SheetDescription>
            {factura
              ? `Folio ${factura.serieProveedor ?? ''}${factura.folioProveedor ?? '—'} — total ${factura.total.toFixed(2)} ${factura.moneda}. La cancelación es irreversible.`
              : 'Selecciona una factura.'}
          </SheetDescription>
        </SheetHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
          <div className="space-y-1">
            <Label htmlFor="motivo">Motivo</Label>
            <Controller
              control={form.control}
              name="motivo"
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                >
                  <SelectTrigger id="motivo" aria-label="Motivo">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MOTIVOS_DISPONIBLES.map((m) => (
                      <SelectItem key={m} value={String(m)}>
                        {MotivoCancelacionLabels[m]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>

          <div className="space-y-1">
            <Label htmlFor="texto">
              Texto explicativo
              {requiereTexto && (
                <span className="ml-1 text-destructive">*</span>
              )}
            </Label>
            <Textarea
              id="texto"
              rows={4}
              placeholder={
                requiereTexto
                  ? 'Detalle obligatorio para motivo "Otro"…'
                  : 'Opcional. Si el motivo no es "Otro" puede quedar vacío.'
              }
              aria-invalid={textoError != null}
              aria-describedby={textoError ? 'texto-error' : undefined}
              {...form.register('texto')}
            />
            {textoError && (
              <p id="texto-error" className="text-xs text-destructive">
                {textoError.message}
              </p>
            )}
          </div>

          <SheetFooter className="px-0">
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={cancelar.isPending}
            >
              Volver
            </Button>
            <Button
              type="submit"
              variant="destructive"
              disabled={cancelar.isPending || !factura}
            >
              {cancelar.isPending ? 'Cancelando…' : 'Cancelar factura'}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
