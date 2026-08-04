import { useState } from 'react';
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
import { Input } from '@/components/ui/input';
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
  AdjuntarEvidenciaSchema,
  type AdjuntarEvidenciaValues,
} from '@/features/cxp/schemas/revision';
import { useAdjuntarEvidencia } from '@/features/cxp/api/useRevision';
import {
  EstadoFirmaFisica,
  EstadoFirmaFisicaLabels,
  TipoEvidencia,
  TipoEvidenciaLabels,
  type FacturaDetalle,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;AdjuntarEvidenciaSheet/&gt;</c> — slide-from-right para adjuntar
 * una evidencia de autorización informal a una factura (captura
 * WhatsApp / audio / email / firma escaneada / otro). El upload va al
 * blob storage del módulo + persistencia de metadata.
 *
 * <para>Si el tipo es firma escaneada (autorización informal acordada
 * pero la firma física llegará después), el usuario marca
 * <c>EstadoFirmaFisica = Pendiente</c> y debe indicar fecha límite.
 * Esto activa el flujo "bandeja de autorizaciones con firma pendiente"
 * (PLATFORM-TODO &lt;BandejaFirmasPendientes&gt;).</para>
 */
export interface AdjuntarEvidenciaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  factura: FacturaDetalle | null;
}

const TIPOS: readonly TipoEvidencia[] = [
  TipoEvidencia.CapturaWhatsapp,
  TipoEvidencia.Audio,
  TipoEvidencia.Email,
  TipoEvidencia.FirmaEscaneada,
  TipoEvidencia.Otro,
];

const ESTADOS_FIRMA: readonly EstadoFirmaFisica[] = [
  EstadoFirmaFisica.NoAplica,
  EstadoFirmaFisica.Pendiente,
  EstadoFirmaFisica.Recibida,
];

/**
 * Patrón "remount on open" (React 19 friendly): el sheet exterior se
 * suscribe a <c>open</c> y monta/desmonta el formulario interno con un
 * <c>key</c> derivado, evitando <c>setState</c> dentro de <c>useEffect</c>
 * (rule <c>react-hooks/set-state-in-effect</c>).
 */
export function AdjuntarEvidenciaSheet({
  open,
  onOpenChange,
  factura,
}: AdjuntarEvidenciaSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>Adjuntar evidencia de autorización</SheetTitle>
          <SheetDescription>
            {factura
              ? `Folio ${factura.serieProveedor ?? ''}${factura.folioProveedor ?? '—'}. Adjunta captura/audio/email + comentario.`
              : 'Selecciona una factura.'}
          </SheetDescription>
        </SheetHeader>
        {open && factura && (
          <AdjuntarEvidenciaForm
            key={factura.id}
            factura={factura}
            onClose={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

interface AdjuntarEvidenciaFormProps {
  factura: FacturaDetalle;
  onClose: () => void;
}

function AdjuntarEvidenciaForm({
  factura,
  onClose,
}: AdjuntarEvidenciaFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const adjuntar = useAdjuntarEvidencia();
  const [archivo, setArchivo] = useState<File | null>(null);
  const [archivoError, setArchivoError] = useState<string | null>(null);

  const form = useForm<AdjuntarEvidenciaValues>({
    resolver: zodResolver(AdjuntarEvidenciaSchema),
    defaultValues: {
      tipo: TipoEvidencia.CapturaWhatsapp,
      comentario: '',
      estadoFirmaFisica: EstadoFirmaFisica.NoAplica,
      fechaLimiteFirmaFisica: null,
    },
  });

  const estadoFirma = useWatch({
    control: form.control,
    name: 'estadoFirmaFisica',
  });

  function onSubmit(values: AdjuntarEvidenciaValues) {
    if (!archivo) {
      setArchivoError('Selecciona un archivo (PDF, imagen, audio…).');
      return;
    }
    adjuntar.mutate(
      {
        facturaId: factura.id,
        tipo: values.tipo,
        comentario: values.comentario,
        estadoFirmaFisica: values.estadoFirmaFisica,
        fechaLimiteFirmaFisica: values.fechaLimiteFirmaFisica ?? null,
        archivo,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Evidencia adjuntada');
          onClose();
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
          toast.error('Error al adjuntar la evidencia.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
          <div className="space-y-1">
            <Label htmlFor="tipo">Tipo de evidencia</Label>
            <Controller
              control={form.control}
              name="tipo"
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                >
                  <SelectTrigger id="tipo">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {TIPOS.map((t) => (
                      <SelectItem key={t} value={String(t)}>
                        {TipoEvidenciaLabels[t]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>

          <div className="space-y-1">
            <Label htmlFor="archivo">Archivo</Label>
            <Input
              id="archivo"
              type="file"
              accept="image/*,application/pdf,audio/*,message/rfc822"
              onChange={(e) => {
                setArchivo(e.target.files?.[0] ?? null);
                setArchivoError(null);
              }}
            />
            {archivoError && (
              <p className="text-xs text-destructive">{archivoError}</p>
            )}
            {archivo && (
              <p className="text-xs text-muted-foreground">
                {archivo.name} · {(archivo.size / 1024).toFixed(1)} KB
              </p>
            )}
          </div>

          <div className="space-y-1">
            <Label htmlFor="comentario">Comentario *</Label>
            <Textarea
              id="comentario"
              rows={4}
              placeholder="Quién autorizó, cuándo, por qué medio (ej.: WhatsApp del Director Operaciones, 2026-05-24)."
              {...form.register('comentario')}
            />
            {form.formState.errors.comentario && (
              <p className="text-xs text-destructive">
                {form.formState.errors.comentario.message}
              </p>
            )}
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <Label htmlFor="estadoFirmaFisica">Estado firma física</Label>
              <Controller
                control={form.control}
                name="estadoFirmaFisica"
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(v) => field.onChange(Number(v))}
                  >
                    <SelectTrigger id="estadoFirmaFisica">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {ESTADOS_FIRMA.map((s) => (
                        <SelectItem key={s} value={String(s)}>
                          {EstadoFirmaFisicaLabels[s]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-1">
              <Label htmlFor="fechaLimiteFirmaFisica">
                Fecha límite
                {estadoFirma === EstadoFirmaFisica.Pendiente && (
                  <span className="ml-1 text-destructive">*</span>
                )}
              </Label>
              <Input
                id="fechaLimiteFirmaFisica"
                type="date"
                disabled={estadoFirma !== EstadoFirmaFisica.Pendiente}
                {...form.register('fechaLimiteFirmaFisica')}
              />
              {form.formState.errors.fechaLimiteFirmaFisica && (
                <p className="text-xs text-destructive">
                  {form.formState.errors.fechaLimiteFirmaFisica.message}
                </p>
              )}
            </div>
          </div>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={adjuntar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={adjuntar.isPending}>
          {adjuntar.isPending ? 'Adjuntando…' : 'Adjuntar evidencia'}
        </Button>
      </SheetFooter>
    </form>
  );
}
