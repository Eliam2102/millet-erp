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
import { esApiError } from '@/lib/api';
import {
  RegularizarValeSchema,
  type RegularizarValeValues,
} from '@/features/almacen/schemas/salida';
import { useRegularizarVale } from '@/features/almacen/api/useSalidas';
import { Field } from '@/features/almacen/components/internal/Field';

/**
 * <c>&lt;RegularizarValeSheet/&gt;</c> — sheet para vincular una RQ
 * aprobada posterior a una salida por vale (A14, SLA 48h). El
 * backend valida que la RQ esté aprobada y aplica el vínculo.
 *
 * <para>Permiso: <c>almacen.salidas.por-vale</c> (mismo que registra
 * el vale inicial — quien regulariza es típicamente el supervisor
 * del solicitante).</para>
 */
export interface RegularizarValeSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  salidaId: string;
  folio: string;
}

export function RegularizarValeSheet({
  open,
  onOpenChange,
  salidaId,
  folio,
}: RegularizarValeSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Regularizar vale {folio}</SheetTitle>
          <SheetDescription>
            Vincula una RQ aprobada posterior al vale para cerrar el
            ciclo (A14). La RQ debe pertenecer al mismo
            sub-almacén/persona destinataria/material del vale.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <FormBody
            salidaId={salidaId}
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function FormBody({
  salidaId,
  onSuccess,
  onCancel,
}: {
  salidaId: string;
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const regularizar = useRegularizarVale();
  const form = useForm<RegularizarValeValues>({
    resolver: zodResolver(RegularizarValeSchema),
    defaultValues: { rqRegularizadoraId: '' },
  });

  function onSubmit(values: RegularizarValeValues) {
    regularizar.mutate(
      {
        command: {
          salidaId,
          rqRegularizadoraId: values.rqRegularizadoraId,
        },
      },
      {
        onSuccess: () => {
          toast.success('Vale regularizado');
          onSuccess();
          form.reset({ rqRegularizadoraId: '' });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'VALE_YA_REGULARIZADO') {
              toast.error('Este vale ya fue regularizado previamente.');
              return;
            }
            if (error.code === 'RQ_NO_APROBADA') {
              form.setError('rqRegularizadoraId', {
                type: error.code,
                message: 'La RQ no está aprobada o no existe.',
              });
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al regularizar.');
        },
      },
    );
  }

  return (
    <>
      <form
        id="regularizar-vale-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-3 overflow-y-auto px-6"
      >
        <Field
          label="RQ regularizadora"
          required
          error={form.formState.errors.rqRegularizadoraId?.message}
        >
          <Controller
            name="rqRegularizadoraId"
            control={form.control}
            render={({ field }) => (
              <Input
                {...field}
                value={field.value ?? ''}
                placeholder="GUID de la RQ aprobada"
                spellCheck={false}
                autoFocus
              />
            )}
          />
        </Field>
        <p className="text-xs text-muted-foreground">
          La RQ debe haber sido aprobada con posterioridad al vale y
          cubrir el mismo material/cantidad. El backend valida el
          vínculo y, si pasa, marca el vale como regularizado.
        </p>
      </form>

      <SheetFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={regularizar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="regularizar-vale-form"
          disabled={regularizar.isPending}
        >
          {regularizar.isPending ? 'Regularizando…' : 'Regularizar'}
        </Button>
      </SheetFooter>
    </>
  );
}
