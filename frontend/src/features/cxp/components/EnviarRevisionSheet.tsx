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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
  EnviarRevisionSchema,
  type EnviarRevisionValues,
} from '@/features/cxp/schemas/revision';
import {
  useEnviarFacturaARevision,
  useMotivosRevision,
} from '@/features/cxp/api/useRevision';
import type { FacturaDetalle } from '@/features/cxp/api/types';

/**
 * <c>&lt;EnviarRevisionSheet/&gt;</c> — slide-from-right que envía una
 * factura a revisión por dependencia. Selecciona motivo del catálogo
 * (con SLA visible) + indica el UUID de la dependencia revisora.
 *
 * <para>PLATFORM-TODO(&lt;DependenciasRevisorasPicker&gt;): hoy se pega
 * el UUID de la dependencia a mano. Cuando se conecte el catálogo de
 * dependencias revisoras (PLATFORM-TODO &lt;DependenciasRevisorasEnAdmin&gt;
 * del backend), agregar combobox.</para>
 */
export interface EnviarRevisionSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  factura: FacturaDetalle | null;
}

export function EnviarRevisionSheet({
  open,
  onOpenChange,
  factura,
}: EnviarRevisionSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const motivos = useMotivosRevision();
  const enviar = useEnviarFacturaARevision();

  const form = useForm<EnviarRevisionValues>({
    resolver: zodResolver(EnviarRevisionSchema),
    defaultValues: {
      motivoRevisionId: '',
      dependenciaRevisoraId: '',
    },
  });

  useEffect(() => {
    if (open) {
      form.reset({
        motivoRevisionId: '',
        dependenciaRevisoraId: '',
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, factura?.id]);

  function onSubmit(values: EnviarRevisionValues) {
    if (!factura) return;
    enviar.mutate(
      {
        id: factura.id,
        versionEsperada: factura.version,
        command: values,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Factura enviada a revisión');
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
          toast.error('Error al enviar a revisión.');
        },
      },
    );
  }

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Enviar a revisión</SheetTitle>
          <SheetDescription>
            {factura
              ? `Folio ${factura.serieProveedor ?? ''}${factura.folioProveedor ?? '—'}. La factura quedará bloqueada hasta que el área la libere.`
              : 'Selecciona una factura.'}
          </SheetDescription>
        </SheetHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
          <div className="space-y-1">
            <Label htmlFor="motivoRevisionId">Motivo</Label>
            <Controller
              control={form.control}
              name="motivoRevisionId"
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="motivoRevisionId" aria-label="Motivo">
                    <SelectValue placeholder="Selecciona un motivo…" />
                  </SelectTrigger>
                  <SelectContent>
                    {motivos.data?.map((m) => (
                      <SelectItem key={m.id} value={m.id}>
                        {m.nombre}
                        {m.slaDias != null && ` · SLA ${m.slaDias}d`}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {form.formState.errors.motivoRevisionId && (
              <p className="text-xs text-destructive">
                {form.formState.errors.motivoRevisionId.message}
              </p>
            )}
            {motivos.isError && (
              <p className="text-xs text-destructive">
                Error al cargar motivos. Reintenta.
              </p>
            )}
          </div>

          <div className="space-y-1">
            <Label htmlFor="dependenciaRevisoraId">
              Dependencia revisora (UUID)
            </Label>
            <Input
              id="dependenciaRevisoraId"
              placeholder="00000000-0000-…"
              autoComplete="off"
              {...form.register('dependenciaRevisoraId')}
            />
            {form.formState.errors.dependenciaRevisoraId && (
              <p className="text-xs text-destructive">
                {form.formState.errors.dependenciaRevisoraId.message}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              Identificador de la dependencia que revisa. Cuando el catálogo
              esté integrado, se reemplaza por un combobox (PLATFORM-TODO
              <code>DependenciasRevisorasPicker</code>).
            </p>
          </div>

          <SheetFooter className="px-0">
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={enviar.isPending}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={enviar.isPending || !factura}>
              {enviar.isPending ? 'Enviando…' : 'Enviar a revisión'}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
