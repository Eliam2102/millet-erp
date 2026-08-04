import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { FileText, Plus } from 'lucide-react';
import { toast } from 'sonner';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { TextAreaField } from '@/components/erp';
import {
  EvidenciaUpload,
  type EvidenciaUploadValue,
} from '@/features/almacen/components/EvidenciaUpload';
import {
  AdjuntarEvidenciaSchema,
  type AdjuntarEvidenciaValues,
} from '@/features/almacen/schemas/devolucion';
import { useAdjuntarEvidenciaProveedor } from '@/features/almacen/api/useDevolucionesProveedor';
import type { DevolucionProveedorEvidenciaItem } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { Field } from '@/features/almacen/components/internal/Field';
import { cn } from '@/lib/utils';

const TIPOS_EVIDENCIA = [
  'Foto del defecto',
  'Email con proveedor',
  'Carta de no conformidad',
  'Video',
  'Reporte de calidad',
  'Otro',
] as const;

export interface EvidenciasManagerProps {
  devolucionId: string;
  evidencias: readonly DevolucionProveedorEvidenciaItem[];
  /** Si <c>false</c>, oculta el botón de agregar (estado avanzado). */
  canAdd: boolean;
}

/**
 * <c>&lt;EvidenciasManager/&gt;</c> — lista de evidencias adjuntas a
 * una devolución a proveedor + botón "Agregar evidencia" que abre un
 * dialog con upload + metadata.
 *
 * <para>El backend exige al menos una evidencia antes de pasar a
 * <c>EnAutorizacion</c>. La lista es read-only por ahora (sin remover);
 * el remover queda diferido a hardening.</para>
 */
export function EvidenciasManager({
  devolucionId,
  evidencias,
  canAdd,
}: EvidenciasManagerProps) {
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">
          Evidencias ({evidencias.length})
        </h3>
        {canAdd && (
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => setDialogOpen(true)}
          >
            <Plus className="mr-2 h-4 w-4" />
            Agregar evidencia
          </Button>
        )}
      </div>

      {evidencias.length === 0 ? (
        <div
          className={cn(
            'rounded-md border border-dashed bg-muted/30 px-4 py-6 text-center text-sm text-muted-foreground',
          )}
        >
          Sin evidencias adjuntas. Agrega al menos una antes de solicitar
          autorización.
        </div>
      ) : (
        <ul className="divide-y rounded-md border">
          {evidencias.map((e) => (
            <li key={e.id} className="flex items-start gap-3 px-3 py-2">
              <FileText className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">{e.nombreArchivo}</p>
                <p className="text-xs text-muted-foreground">
                  {e.tipoEvidencia}
                  {e.comentario ? ` · ${e.comentario}` : ''}
                </p>
                <p className="mt-0.5 truncate text-xs text-muted-foreground/80 font-mono">
                  {e.blobRef}
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}

      <AgregarEvidenciaDialog
        devolucionId={devolucionId}
        open={dialogOpen}
        onOpenChange={setDialogOpen}
      />
    </section>
  );
}

function AgregarEvidenciaDialog({
  devolucionId,
  open,
  onOpenChange,
}: {
  devolucionId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Agregar evidencia</DialogTitle>
          <DialogDescription>
            Sube el archivo y captura su tipo y comentario opcional.
          </DialogDescription>
        </DialogHeader>
        {open && (
          <AgregarEvidenciaBody
            devolucionId={devolucionId}
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function AgregarEvidenciaBody({
  devolucionId,
  onSuccess,
  onCancel,
}: {
  devolucionId: string;
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const [adjunto, setAdjunto] = useState<EvidenciaUploadValue | null>(null);
  const adjuntar = useAdjuntarEvidenciaProveedor();

  const form = useForm<AdjuntarEvidenciaValues>({
    resolver: zodResolver(AdjuntarEvidenciaSchema),
    defaultValues: { tipoEvidencia: 'Foto del defecto', comentario: null },
  });

  function onSubmit(values: AdjuntarEvidenciaValues) {
    if (adjunto == null) {
      toast.error('Sube el archivo de la evidencia antes de adjuntar.');
      return;
    }
    adjuntar.mutate(
      {
        command: {
          devolucionId,
          tipoEvidencia: values.tipoEvidencia,
          nombreArchivo: adjunto.nombreArchivo,
          blobRef: adjunto.blobRef,
          comentario: values.comentario ?? null,
        },
      },
      {
        onSuccess: () => {
          toast.success('Evidencia adjuntada');
          onSuccess();
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al adjuntar.');
        },
      },
    );
  }

  return (
    <>
      <form
        id="agregar-evidencia-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="space-y-3"
      >
        <Field
          label="Tipo de evidencia"
          required
          error={form.formState.errors.tipoEvidencia?.message}
        >
          <Controller
            name="tipoEvidencia"
            control={form.control}
            render={({ field }) => (
              <Select
                value={field.value || ''}
                onValueChange={(v) => field.onChange(v)}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {TIPOS_EVIDENCIA.map((t) => (
                    <SelectItem key={t} value={t}>
                      {t}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </Field>

        <Field label="Archivo" required>
          <EvidenciaUpload value={adjunto} onChange={setAdjunto} />
        </Field>

        <Field
          label="Comentario (opcional)"
          error={form.formState.errors.comentario?.message}
        >
          <Controller
            name="comentario"
            control={form.control}
            render={({ field }) => (
              <TextAreaField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v)}
                maxLength={500}
                minRows={2}
              />
            )}
          />
        </Field>
      </form>

      <DialogFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={adjuntar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="agregar-evidencia-form"
          disabled={adjuntar.isPending || adjunto == null}
        >
          {adjuntar.isPending ? 'Adjuntando…' : 'Adjuntar'}
        </Button>
      </DialogFooter>
    </>
  );
}

