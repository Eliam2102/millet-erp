import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
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
import { CfdiOriginalPicker } from '@/components/erp/selectors/CfdiOriginalPicker';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  MarcarCfdiDuplicadoSchema,
  type MarcarCfdiDuplicadoValues,
} from '@/features/cxp/schemas/cfdi';
import { useMarcarCfdiDuplicado } from '@/features/cxp/api/useCfdis';
import type { CfdiListItem } from '@/features/cxp/api/types';

/**
 * <c>&lt;MarcarDuplicadoSheet/&gt;</c> — slide-from-right que marca un
 * CFDI como duplicado de otro previamente ingresado. El usuario indica
 * el <c>cfdiOriginalId</c> (UUID del CfdiRecibido original) y el
 * backend valida que ambos existan y estén en el estado correcto.
 *
 * <para>Usa <c>&lt;CfdiOriginalPicker/&gt;</c> para buscar el CFDI
 * original por folio/UUID SAT/fecha, pre-filtrando por el mismo
 * <c>rfcEmisor</c> del duplicado (PLATFORM-TODO
 * &lt;CfdiOriginalPicker&gt; cerrado).</para>
 */
export interface MarcarDuplicadoSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cfdi: CfdiListItem | null;
}

export function MarcarDuplicadoSheet({
  open,
  onOpenChange,
  cfdi,
}: MarcarDuplicadoSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const marcar = useMarcarCfdiDuplicado();

  const form = useForm<MarcarCfdiDuplicadoValues>({
    resolver: zodResolver(MarcarCfdiDuplicadoSchema),
    defaultValues: { cfdiOriginalId: '' },
  });

  useEffect(() => {
    if (open) {
      form.reset({ cfdiOriginalId: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, cfdi?.id]);

  function onSubmit(values: MarcarCfdiDuplicadoValues) {
    if (!cfdi) return;
    marcar.mutate(
      { id: cfdi.id, command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('CFDI marcado como duplicado');
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
          toast.error('Error inesperado al marcar duplicado.');
        },
      },
    );
  }

  const idError = form.formState.errors.cfdiOriginalId;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Marcar CFDI como duplicado</SheetTitle>
          <SheetDescription>
            {cfdi
              ? `UUID ${cfdi.uuidCfdi} — emisor ${cfdi.rfcEmisor}. Indica el CFDI original al que duplica.`
              : 'Selecciona un CFDI desde la bandeja.'}
          </SheetDescription>
        </SheetHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
          <div className="space-y-1">
            <Label htmlFor="cfdiOriginalId">CFDI original</Label>
            <Controller
              control={form.control}
              name="cfdiOriginalId"
              render={({ field }) => (
                <CfdiOriginalPicker
                  value={field.value}
                  onChange={(id) => field.onChange(id ?? '')}
                  rfcEmisor={cfdi?.rfcEmisor}
                  currentCfdiId={cfdi?.id}
                />
              )}
            />
            {idError && (
              <p
                id="cfdiOriginalId-error"
                className="text-xs text-destructive"
              >
                {idError.message}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              Sólo se muestran CFDIs convertidos en pasivo del mismo emisor.
            </p>
          </div>

          <SheetFooter className="px-0">
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={marcar.isPending}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={marcar.isPending || !cfdi}>
              {marcar.isPending ? 'Marcando…' : 'Marcar duplicado'}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
