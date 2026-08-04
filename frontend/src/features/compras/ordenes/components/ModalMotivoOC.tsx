import { useState } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { TextAreaField } from '@/components/erp';
import {
  MotivoRechazoSelector,
  MotivoRechazoAplicaA,
} from '@/features/compras/components/MotivoRechazoSelector';
import type { MotivoRechazoResponse } from '@/features/compras/api/types';

/**
 * <c>&lt;ModalMotivoOC/&gt;</c> — modal reutilizable para capturar
 * motivo + texto en flujos de Rechazar / Cancelar OC.
 *
 * <para>Reusa <c>&lt;MotivoRechazoSelector/&gt;</c> de RQ con filtro
 * <c>aplicaA</c> parametrizado por el caller (Rechazo/Cancelación/
 * OrdenCompra). Si el motivo seleccionado tiene
 * <c>permiteTextoLibre=true</c>, el textarea de detalle se vuelve
 * obligatorio (≥3 chars).</para>
 *
 * <para>El submit emite <c>{motivoId, motivoTexto}</c> genérico; el
 * caller mapea al shape de su schema (<c>{motivoRechazoId,...}</c> o
 * <c>{motivoCancelacionId,...}</c>) antes de invocar el mutation.</para>
 */

const ModalMotivoSchema = z.object({
  motivoId: z.string().min(1, 'Selecciona un motivo.'),
  motivoTexto: z.string().max(500).nullish(),
});

export type ModalMotivoValues = z.infer<typeof ModalMotivoSchema>;

export interface ModalMotivoOCProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titulo: string;
  descripcion: string;
  /**
   * Bitmask del flujo (Rechazo, Cancelacion, OrdenCompra). Filtra qué
   * motivos del catálogo aparecen en el selector.
   */
  aplicaA: MotivoRechazoAplicaA;
  /** Texto del botón de confirmación. Default "Confirmar". */
  ctaLabel?: string;
  /** Llamado al confirmar con motivoId + motivoTexto opcional. */
  onSubmit: (values: ModalMotivoValues) => Promise<void>;
  /** Indicador de loading externo (el mutation del caller). */
  isPending?: boolean;
}

export function ModalMotivoOC({
  open,
  onOpenChange,
  titulo,
  descripcion,
  aplicaA,
  ctaLabel = 'Confirmar',
  onSubmit,
  isPending,
}: ModalMotivoOCProps) {
  const [motivoSeleccionado, setMotivoSeleccionado] =
    useState<MotivoRechazoResponse | null>(null);

  const form = useForm<ModalMotivoValues>({
    resolver: zodResolver(ModalMotivoSchema),
    defaultValues: {
      motivoId: '',
      motivoTexto: null,
    },
  });

  const permiteTextoLibre = motivoSeleccionado?.permiteTextoLibre ?? false;

  async function handleSubmit(values: ModalMotivoValues) {
    if (
      permiteTextoLibre &&
      (!values.motivoTexto || values.motivoTexto.trim().length < 3)
    ) {
      form.setError('motivoTexto', {
        type: 'manual',
        message: 'El motivo requiere un texto descriptivo (≥3 caracteres).',
      });
      return;
    }
    await onSubmit(values);
    if (!form.formState.isSubmitting) {
      form.reset();
      setMotivoSeleccionado(null);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!isPending) onOpenChange(o);
      }}
    >
      <DialogContent
        className="max-w-lg"
        data-component="modal-motivo-oc"
      >
        <DialogHeader>
          <DialogTitle>{titulo}</DialogTitle>
          <DialogDescription>{descripcion}</DialogDescription>
        </DialogHeader>

        <form
          onSubmit={form.handleSubmit(handleSubmit)}
          noValidate
          className="space-y-3"
        >
          <FieldGroup
            label="Motivo"
            required
            error={form.formState.errors.motivoId?.message}
          >
            <Controller
              name="motivoId"
              control={form.control}
              render={({ field }) => (
                <MotivoRechazoSelector
                  aplicaA={aplicaA}
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  onMotivoChange={setMotivoSeleccionado}
                  disabled={isPending}
                />
              )}
            />
          </FieldGroup>

          {permiteTextoLibre && (
            <FieldGroup
              label="Detalle del motivo"
              required
              error={form.formState.errors.motivoTexto?.message}
            >
              <Controller
                name="motivoTexto"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    value={field.value || null}
                    onChange={(v) => field.onChange(v || null)}
                    maxLength={500}
                    minRows={2}
                    disabled={isPending}
                  />
                )}
              />
            </FieldGroup>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isPending}
            >
              Cancelar
            </Button>
            <Button
              type="submit"
              variant="destructive"
              disabled={isPending}
              data-action="confirmar-motivo-oc"
            >
              {isPending ? 'Procesando…' : ctaLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function FieldGroup({
  label,
  required,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label className="flex items-center gap-1 text-xs font-medium text-foreground">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
