import { useEffect } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { useRegistrarSeguimientoCobranza } from '@/features/cxc/api/useCobranza';
import {
  CanalCobranza,
  ResultadoCobranza,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_CANAL_COBRANZA,
  ETIQUETA_RESULTADO_COBRANZA,
} from '@/features/cxc/lib/glosario';
import {
  GestionCobranzaSchema,
  type GestionCobranzaValues,
} from '@/features/cxc/schemas/gestion-cobranza';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';

/**
 * <c>&lt;RegistrarGestion/&gt;</c> — form del Sheet "Registrar gestión
 * de cobranza" (CXC-FE-PR4). Append-only: lo registrado no se edita ni
 * borra. Promesa de pago exige monto y fecha comprometidos; en los
 * demás resultados esos campos se ocultan y viajan como null (espejo
 * <c>SC_COMPROMISO_SOBRANTE</c>).
 */
export interface RegistrarGestionProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
  /** Cliente preseleccionado al abrir desde /cxc/cobranza. */
  clienteIdInicial?: string | null;
}

export function RegistrarGestion({
  onClose,
  onDirtyChange,
  clienteIdInicial,
}: RegistrarGestionProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarSeguimientoCobranza();

  const form = useForm<GestionCobranzaValues>({
    resolver: zodResolver(GestionCobranzaSchema),
    defaultValues: {
      clienteId: clienteIdInicial ?? '',
      canal: CanalCobranza.Llamada,
      resultado: ResultadoCobranza.SinRespuesta,
      montoComprometido: null,
      fechaComprometida: null,
      nota: '',
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const resultado = useWatch({ control: form.control, name: 'resultado' });
  const esPromesa = resultado === ResultadoCobranza.PromesaPago;

  // Al salir de "Promesa de pago" se limpian los campos del compromiso
  // (el backend los rechaza en cualquier otro resultado).
  useEffect(() => {
    if (!esPromesa) {
      form.setValue('montoComprometido', null, { shouldDirty: false });
      form.setValue('fechaComprometida', null, { shouldDirty: false });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [esPromesa]);

  function onSubmit(values: GestionCobranzaValues) {
    registrar.mutate(
      {
        command: {
          clienteId: values.clienteId,
          canal: values.canal as CanalCobranza,
          resultado: values.resultado as ResultadoCobranza,
          montoComprometido: esPromesa ? values.montoComprometido : null,
          fechaComprometida: esPromesa
            ? values.fechaComprometida || null
            : null,
          nota: values.nota.trim(),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            `Gestión registrada (${ETIQUETA_RESULTADO_COBRANZA[res.resultado]}).`,
          );
          onClose({ force: true });
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
              description:
                error.problem.detail ??
                (error.traceId ? `Código: ${error.traceId}` : undefined),
            });
            return;
          }
          toast.error('Error inesperado al registrar la gestión.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div className="space-y-1 sm:col-span-2">
          <Label className="text-xs">
            Cliente <span className="text-destructive">*</span>
          </Label>
          <Controller
            name="clienteId"
            control={form.control}
            render={({ field }) => (
              <ClienteSelectorCxc
                value={field.value || null}
                onChange={(item) => field.onChange(item?.id ?? '')}
                initialLabel={
                  clienteIdInicial ? 'Cliente de la bitácora abierta' : undefined
                }
              />
            )}
          />
          {form.formState.errors.clienteId && (
            <p className="text-xs text-destructive">
              {form.formState.errors.clienteId.message}
            </p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="canal" className="text-xs">
            Canal <span className="text-destructive">*</span>
          </Label>
          <select
            id="canal"
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('canal', { valueAsNumber: true })}
          >
            {Object.values(CanalCobranza).map((c) => (
              <option key={c} value={c}>
                {ETIQUETA_CANAL_COBRANZA[c]}
              </option>
            ))}
          </select>
        </div>

        <div className="space-y-1">
          <Label htmlFor="resultado" className="text-xs">
            Resultado <span className="text-destructive">*</span>
          </Label>
          <select
            id="resultado"
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('resultado', { valueAsNumber: true })}
          >
            {Object.values(ResultadoCobranza).map((r) => (
              <option key={r} value={r}>
                {ETIQUETA_RESULTADO_COBRANZA[r]}
              </option>
            ))}
          </select>
        </div>

        {esPromesa && (
          <>
            <div className="space-y-1">
              <Label htmlFor="montoComprometido" className="text-xs">
                Monto comprometido <span className="text-destructive">*</span>
              </Label>
              <Input
                id="montoComprometido"
                type="number"
                step="0.01"
                min="0"
                {...form.register('montoComprometido', {
                  setValueAs: (v) => {
                    if (v === '' || v == null) return null;
                    const n = Number(v);
                    return Number.isNaN(n) ? null : n;
                  },
                })}
              />
              {form.formState.errors.montoComprometido && (
                <p className="text-xs text-destructive">
                  {form.formState.errors.montoComprometido.message}
                </p>
              )}
            </div>
            <div className="space-y-1">
              <Label htmlFor="fechaComprometida" className="text-xs">
                Fecha comprometida <span className="text-destructive">*</span>
              </Label>
              <Input
                id="fechaComprometida"
                type="date"
                {...form.register('fechaComprometida', {
                  setValueAs: (v) => (v === '' || v == null ? null : v),
                })}
              />
              {form.formState.errors.fechaComprometida && (
                <p className="text-xs text-destructive">
                  {form.formState.errors.fechaComprometida.message}
                </p>
              )}
            </div>
          </>
        )}

        <div className="space-y-1 sm:col-span-2">
          <Label htmlFor="nota-gestion" className="text-xs">
            Nota de la gestión <span className="text-destructive">*</span>
          </Label>
          <Controller
            name="nota"
            control={form.control}
            render={({ field }) => (
              <TextAreaField
                value={field.value}
                onChange={(v) => field.onChange(v ?? '')}
                maxLength={2000}
                textareaProps={{
                  id: 'nota-gestion',
                  placeholder:
                    'Qué se habló, con quién, acuerdos… (queda en la bitácora tal cual)',
                }}
              />
            )}
          />
          {form.formState.errors.nota && (
            <p className="text-xs text-destructive">
              {form.formState.errors.nota.message}
            </p>
          )}
        </div>
      </section>

      <p className="text-xs text-muted-foreground">
        La bitácora es append-only: la gestión no podrá editarse ni
        borrarse después de registrarla.
      </p>

      <div className="flex items-center justify-end gap-2 border-t pt-4">
        <Button
          type="button"
          variant="ghost"
          onClick={() => onClose()}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={registrar.isPending}>
          {registrar.isPending ? 'Registrando…' : 'Registrar gestión'}
        </Button>
      </div>
    </form>
  );
}
