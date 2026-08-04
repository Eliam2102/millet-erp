import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
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
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  DescartarCfdiSchema,
  type DescartarCfdiValues,
} from '@/features/cxp/schemas/cfdi';
import { useDescartarCfdi } from '@/features/cxp/api/useCfdis';
import type { CfdiListItem } from '@/features/cxp/api/types';

/**
 * <c>&lt;DescartarCfdiSheet/&gt;</c> — slide-from-right que confirma el
 * descarte de un CFDI en estado <c>PorProcesar</c> con motivo libre
 * obligatorio.
 *
 * <para>El backend valida que el CFDI esté en estado correcto; si no,
 * devuelve <c>CFDI_NO_PUEDE_DESCARTARSE</c> y mostramos toast con
 * código. El motivo libre queda en auditoría (no se valida contenido,
 * sólo longitud 5-500).</para>
 */
export interface DescartarCfdiSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cfdi: CfdiListItem | null;
}

export function DescartarCfdiSheet({
  open,
  onOpenChange,
  cfdi,
}: DescartarCfdiSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const descartar = useDescartarCfdi();

  const form = useForm<DescartarCfdiValues>({
    resolver: zodResolver(DescartarCfdiSchema),
    defaultValues: { motivo: '' },
  });

  useEffect(() => {
    if (open) {
      form.reset({ motivo: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, cfdi?.id]);

  function onSubmit(values: DescartarCfdiValues) {
    if (!cfdi) return;
    descartar.mutate(
      { id: cfdi.id, command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('CFDI descartado');
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
          toast.error('Error inesperado al descartar el CFDI.');
        },
      },
    );
  }

  const motivoError = form.formState.errors.motivo;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Descartar CFDI</SheetTitle>
          <SheetDescription>
            {cfdi
              ? `UUID ${cfdi.uuidCfdi} — emisor ${cfdi.rfcEmisor}. Esta acción es irreversible para este CFDI.`
              : 'Selecciona un CFDI desde la bandeja.'}
          </SheetDescription>
        </SheetHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
          <div className="space-y-1">
            <Label htmlFor="motivo">Motivo del descarte</Label>
            <Textarea
              id="motivo"
              rows={5}
              placeholder="Ejemplo: CFDI duplicado de proveedor por reemisión post-cancelación; no aplica al pasivo."
              aria-invalid={motivoError != null}
              aria-describedby={motivoError ? 'motivo-error' : undefined}
              {...form.register('motivo')}
            />
            {motivoError && (
              <p id="motivo-error" className="text-xs text-destructive">
                {motivoError.message}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              Mínimo 5 y máximo 500 caracteres. Queda registrado para auditoría.
            </p>
          </div>

          <SheetFooter className="px-0">
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={descartar.isPending}
            >
              Cancelar
            </Button>
            <Button
              type="submit"
              variant="destructive"
              disabled={descartar.isPending || !cfdi}
            >
              {descartar.isPending ? 'Descartando…' : 'Descartar'}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
