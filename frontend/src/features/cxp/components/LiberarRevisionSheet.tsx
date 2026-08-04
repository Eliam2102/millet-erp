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
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  LiberarRevisionSchema,
  type LiberarRevisionValues,
} from '@/features/cxp/schemas/revision';
import { useLiberarRevisionFactura } from '@/features/cxp/api/useRevision';
import type { FacturaDetalle } from '@/features/cxp/api/types';

/**
 * <c>&lt;LiberarRevisionSheet/&gt;</c> — slide-from-right que libera la
 * factura desde revisión. El responsable del área documenta qué se hizo
 * (texto libre) y la factura vuelve a su flujo normal.
 *
 * <para>El texto queda en auditoría; el backend no enviará evento de
 * negocio adicional, sólo cambia de estado a Capturada y limpia
 * dependenciaRevisora/motivoRevision.</para>
 */
export interface LiberarRevisionSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  factura: FacturaDetalle | null;
}

export function LiberarRevisionSheet({
  open,
  onOpenChange,
  factura,
}: LiberarRevisionSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const liberar = useLiberarRevisionFactura();

  const form = useForm<LiberarRevisionValues>({
    resolver: zodResolver(LiberarRevisionSchema),
    defaultValues: { accionTomada: '' },
  });

  useEffect(() => {
    if (open) {
      form.reset({ accionTomada: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, factura?.id]);

  function onSubmit(values: LiberarRevisionValues) {
    if (!factura) return;
    liberar.mutate(
      {
        id: factura.id,
        versionEsperada: factura.version,
        command: values,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Revisión liberada');
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
          toast.error('Error al liberar revisión.');
        },
      },
    );
  }

  const error = form.formState.errors.accionTomada;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Liberar revisión</SheetTitle>
          <SheetDescription>
            {factura
              ? `Folio ${factura.serieProveedor ?? ''}${factura.folioProveedor ?? '—'}. Documenta brevemente qué decisión tomaste.`
              : 'Selecciona una factura.'}
          </SheetDescription>
        </SheetHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
          <div className="space-y-1">
            <Label htmlFor="accionTomada">Acción tomada</Label>
            <Textarea
              id="accionTomada"
              rows={5}
              placeholder="Ej.: Verificado contra cotización original; el proveedor reconoce el precio."
              aria-invalid={error != null}
              aria-describedby={error ? 'accionTomada-error' : undefined}
              {...form.register('accionTomada')}
            />
            {error && (
              <p id="accionTomada-error" className="text-xs text-destructive">
                {error.message}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              Mínimo 5 caracteres, máximo 2000. Queda en auditoría.
            </p>
          </div>

          <SheetFooter className="px-0">
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={liberar.isPending}
            >
              Volver
            </Button>
            <Button type="submit" disabled={liberar.isPending || !factura}>
              {liberar.isPending ? 'Liberando…' : 'Liberar revisión'}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
