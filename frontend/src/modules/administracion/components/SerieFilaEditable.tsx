import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  ActualizarSerieSchema,
  type ActualizarSerieValues,
} from '@/modules/administracion/schemas/serie';
import {
  useActualizarSerie,
  useSerie,
} from '@/modules/administracion/api';
import {
  ReinicioPeriodo,
  type SerieResponse,
} from '@/modules/administracion/api/types';
import { REINICIO_PERIODO_LABEL } from '@/modules/administracion/components/series-labels';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;SerieFilaEditable/&gt;</c> — inline edit form de una serie
 * (UF-Admin-PR6 §5.2). Border dashed amber (consistente con el patrón
 * cross-módulo de inline edit). Solo Prefijo, Sufijo y ReinicioPeriodo
 * son editables; Empresa, Sucursal y TipoDocumento son inmutables
 * post-alta y se muestran read-only en la fila padre.
 *
 * <para><b>Confirm dialog OBLIGATORIO al cambiar ReinicioPeriodo</b>
 * (UX defensiva): cambiar el reinicio puede dejar folios huérfanos en
 * el período anterior. El backend NO valida esto — el guard vive en
 * la UI.</para>
 *
 * <para>El <c>proximoFolioPreview</c> se obtiene via <c>useSerie(id)</c>
 * y se muestra como caption read-only debajo del form para que el
 * usuario vea el efecto del cambio (refresca tras el PATCH gracias a
 * la invalidación del detail).</para>
 */
export interface SerieFilaEditableProps {
  serie: SerieResponse;
  onCancel: () => void;
  onSaved?: () => void;
}

const REINICIO_OPTIONS: readonly ReinicioPeriodo[] = [
  ReinicioPeriodo.None,
  ReinicioPeriodo.Anual,
  ReinicioPeriodo.Mensual,
];

export function SerieFilaEditable({
  serie,
  onCancel,
  onSaved,
}: SerieFilaEditableProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarSerie();
  const detalleQuery = useSerie(serie.id);
  const [confirmReinicioOpen, setConfirmReinicioOpen] = useState(false);
  const [valoresPendientes, setValoresPendientes] =
    useState<ActualizarSerieValues | null>(null);

  const form = useForm<ActualizarSerieValues>({
    resolver: zodResolver(ActualizarSerieSchema),
    defaultValues: {
      prefijo: serie.prefijo,
      sufijo: serie.sufijo,
      reinicioPeriodo: serie.reinicioPeriodo,
    },
  });

  useEffect(() => {
    form.setFocus('prefijo');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = actualizar.isPending;

  function ejecutarPatch(values: ActualizarSerieValues) {
    const sufijoTrim = values.sufijo == null ? null : values.sufijo;
    const sufijoCambio = sufijoTrim !== serie.sufijo;
    const limpiarSufijo = sufijoCambio && sufijoTrim == null;
    actualizar.mutate(
      {
        id: serie.id,
        payload: {
          prefijo:
            values.prefijo !== serie.prefijo ? values.prefijo : undefined,
          // Si el usuario lo dejó vacío y antes había valor, mandamos
          // limpiarSufijo. Si lo cambió por otro string, va en sufijo.
          sufijo: sufijoCambio && sufijoTrim != null ? sufijoTrim : undefined,
          limpiarSufijo: limpiarSufijo ? true : undefined,
          reinicioPeriodo:
            values.reinicioPeriodo !== serie.reinicioPeriodo
              ? values.reinicioPeriodo
              : undefined,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Serie actualizada');
          setConfirmReinicioOpen(false);
          setValoresPendientes(null);
          onSaved?.();
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'SERIE_DUPLICADA') {
              toast.error('Ya existe una serie con esos datos.');
              setConfirmReinicioOpen(false);
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                err,
              )
            ) {
              setConfirmReinicioOpen(false);
              return;
            }
            toast.error(err.problem.title, {
              description: err.traceId
                ? `Código: ${err.traceId}`
                : undefined,
            });
            setConfirmReinicioOpen(false);
            return;
          }
          toast.error('Error inesperado al actualizar la serie.');
          setConfirmReinicioOpen(false);
        },
      },
    );
  }

  function onSubmit(values: ActualizarSerieValues) {
    if (values.reinicioPeriodo !== serie.reinicioPeriodo) {
      setValoresPendientes(values);
      setConfirmReinicioOpen(true);
      return;
    }
    ejecutarPatch(values);
  }

  const reinicioActual = useWatch({
    control: form.control,
    name: 'reinicioPeriodo',
  });

  return (
    <>
      <form
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        onKeyDown={(e) => {
          if (e.key === 'Escape' && !isPending) {
            e.preventDefault();
            onCancel();
          }
        }}
        className={cn(
          'space-y-3 rounded-md border border-dashed border-amber-400 bg-amber-50/40 p-3',
        )}
        aria-label={`Editar serie ${serie.prefijo}`}
      >
        <div className="grid grid-cols-1 gap-3 md:grid-cols-12">
          <Field
            label="Prefijo"
            required
            error={form.formState.errors.prefijo?.message}
            className="md:col-span-3"
          >
            <Input
              maxLength={10}
              placeholder="OC"
              {...form.register('prefijo')}
            />
          </Field>

          <Field
            label="Sufijo"
            hint="Opcional, máx. 10 caracteres."
            error={form.formState.errors.sufijo?.message}
            className="md:col-span-3"
          >
            <Input
              maxLength={10}
              placeholder=""
              defaultValue={serie.sufijo ?? ''}
              {...form.register('sufijo', {
                setValueAs: (v: unknown) =>
                  typeof v === 'string' && v.trim().length === 0
                    ? null
                    : typeof v === 'string'
                      ? v.trim()
                      : v,
              })}
            />
          </Field>

          <Field
            label="Reinicio de periodo"
            required
            error={form.formState.errors.reinicioPeriodo?.message}
            className="md:col-span-6"
          >
            <div
              role="radiogroup"
              aria-label="Reinicio de periodo"
              className="flex flex-wrap gap-2"
            >
              {REINICIO_OPTIONS.map((value) => {
                const checked = reinicioActual === value;
                return (
                  <label
                    key={value}
                    className={cn(
                      'flex cursor-pointer items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs',
                      checked
                        ? 'border-primary bg-primary/10 font-medium text-primary'
                        : 'border-input bg-background hover:bg-muted/40',
                    )}
                  >
                    <input
                      type="radio"
                      className="sr-only"
                      checked={checked}
                      onChange={() =>
                        form.setValue('reinicioPeriodo', value, {
                          shouldDirty: true,
                          shouldValidate: true,
                        })
                      }
                    />
                    {REINICIO_PERIODO_LABEL[value]}
                  </label>
                );
              })}
            </div>
          </Field>
        </div>

        <p className="text-xs text-muted-foreground">
          Próximo folio:{' '}
          <span className="font-mono">
            {detalleQuery.data?.proximoFolioPreview ?? '—'}
          </span>{' '}
          {detalleQuery.isLoading && '(cargando…)'}
        </p>

        <div className="flex items-center justify-end gap-2 border-t pt-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onCancel}
            disabled={isPending}
          >
            <X className="mr-1 h-4 w-4" />
            Cancelar
          </Button>
          <Button type="submit" size="sm" disabled={isPending}>
            <Check className="mr-1 h-4 w-4" />
            {isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      </form>

      <AlertDialog
        open={confirmReinicioOpen}
        onOpenChange={(open) => {
          if (!open && !isPending) {
            setConfirmReinicioOpen(false);
            setValoresPendientes(null);
          }
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Cambiar reinicio del periodo</AlertDialogTitle>
            <AlertDialogDescription>
              Cambiar el reinicio del periodo puede dejar folios huérfanos en
              el periodo anterior. ¿Confirmas el cambio?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              disabled={isPending}
              onClick={() => {
                if (valoresPendientes != null) {
                  ejecutarPatch(valoresPendientes);
                }
              }}
            >
              {isPending ? 'Guardando…' : 'Confirmar cambio'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

interface FieldProps {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  className?: string;
  children: React.ReactNode;
}

function Field({ label, required, hint, error, className, children }: FieldProps) {
  return (
    <div className={cn('space-y-1', className)}>
      <label className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {hint != null && error == null && (
        <p className="text-xs text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
