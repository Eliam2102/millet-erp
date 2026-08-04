import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
import { Input } from '@/components/ui/input';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import {
  useCrearCatalogo,
  useDetalleCatalogo,
  useEditarCatalogo,
} from '@/features/centros-costo/api/useCatalogoCrud';
import type { GrupoDimDetalle } from '@/features/centros-costo/api/types';
import {
  GrupoDimSchema,
  type GrupoDimValues,
} from '@/features/centros-costo/schemas/catalogo';
import { CatalogoDialogShell } from './CatalogoDialogShell';
import { manejarErrorDialogoCatalogo } from './catalogo-dialog-helpers';
import { Field } from '../internal/Field';

/**
 * Crear/editar un grupo de clasificación — grupos-dim2 y grupos-dim3
 * comparten forma (solo nombre, sin padre: los grupos son globales y
 * clasifican, no anidan). Un solo dialog parametrizado por recurso.
 */
export type GrupoDimDialogModo =
  | { tipo: 'crear' }
  | { tipo: 'editar'; id: string };

interface GrupoDimDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  recurso: 'grupos-dim2' | 'grupos-dim3';
  modo: GrupoDimDialogModo;
}

export function GrupoDimDialog({
  open,
  onOpenChange,
  recurso,
  modo,
}: GrupoDimDialogProps) {
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const etiqueta = etiquetaNivel(
    recurso === 'grupos-dim2' ? 'grupoDim2' : 'grupoDim3',
    'configuracion',
  );

  const detalle = useDetalleCatalogo<GrupoDimDetalle>(
    recurso,
    modo.tipo === 'editar' ? modo.id : null,
  );
  const crear = useCrearCatalogo<GrupoDimValues>(recurso);
  const editar = useEditarCatalogo<GrupoDimValues>(recurso);

  const form = useForm<GrupoDimValues>({
    resolver: zodResolver(GrupoDimSchema),
    defaultValues: { nombre: '' },
  });

  useEffect(() => {
    if (!open) return;
    if (modo.tipo === 'editar' && detalle.data) {
      form.reset({ nombre: detalle.data.data.nombre });
    }
    if (modo.tipo === 'crear') {
      form.reset({ nombre: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, modo.tipo, detalle.data]);

  function onSubmit(values: GrupoDimValues) {
    const opciones = {
      onSuccess: () => {
        toast.success(
          modo.tipo === 'crear' ? `${etiqueta} creado` : `${etiqueta} actualizado`,
        );
        onOpenChange(false);
      },
      onError: (error: unknown) =>
        manejarErrorDialogoCatalogo(error, {
          form: form as unknown as import("@/lib/api/apply-server-errors").FormConSetError,
          campoUnicidad: 'nombre',
          conflictDialog,
          queryClient,
        }),
    };
    if (modo.tipo === 'crear') crear.mutate(values, opciones);
    else editar.mutate({ id: modo.id, body: values }, opciones);
  }

  return (
    <CatalogoDialogShell
      open={open}
      onOpenChange={onOpenChange}
      titulo={modo.tipo === 'crear' ? `Nuevo ${etiqueta}` : `Editar ${etiqueta}`}
      descripcion="Los grupos clasifican, no anidan: desactivar uno solo lo retira de la clasificación nueva (no cascada)."
      formId="grupo-dim-dialog-form"
      onSubmit={form.handleSubmit(onSubmit)}
      submitting={crear.isPending || editar.isPending}
    >
      <Field label="Nombre" htmlFor="grupo-nombre" full required error={form.formState.errors.nombre?.message}>
        <Input {...form.register('nombre')} id="grupo-nombre" maxLength={100} autoFocus />
      </Field>
    </CatalogoDialogShell>
  );
}
