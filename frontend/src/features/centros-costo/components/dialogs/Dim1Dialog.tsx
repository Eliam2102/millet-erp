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
import type { Dim1Detalle } from '@/features/centros-costo/api/types';
import { Dim1Schema, type Dim1Values } from '@/features/centros-costo/schemas/catalogo';
import { CatalogoDialogShell } from './CatalogoDialogShell';
import { manejarErrorDialogoCatalogo } from './catalogo-dialog-helpers';
import { Field } from '../internal/Field';

/** Crear o editar una Dimensión 1 (nivel raíz — sin padre). */
export type Dim1DialogModo =
  | { tipo: 'crear' }
  | { tipo: 'editar'; id: string };

interface Dim1DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  modo: Dim1DialogModo;
}

export function Dim1Dialog({ open, onOpenChange, modo }: Dim1DialogProps) {
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const etiqueta = etiquetaNivel('dim1', 'configuracion');

  const detalle = useDetalleCatalogo<Dim1Detalle>(
    'dim1',
    modo.tipo === 'editar' ? modo.id : null,
  );
  const crear = useCrearCatalogo<Dim1Values>('dim1');
  const editar = useEditarCatalogo<Dim1Values>('dim1');

  const form = useForm<Dim1Values>({
    resolver: zodResolver(Dim1Schema),
    defaultValues: { clave: '', nombre: '' },
  });

  useEffect(() => {
    if (!open) return;
    if (modo.tipo === 'editar' && detalle.data) {
      form.reset({
        clave: detalle.data.data.clave,
        nombre: detalle.data.data.nombre,
      });
    }
    if (modo.tipo === 'crear') {
      form.reset({ clave: '', nombre: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, modo.tipo, detalle.data]);

  function onSubmit(values: Dim1Values) {
    const opciones = {
      onSuccess: () => {
        toast.success(
          modo.tipo === 'crear' ? `${etiqueta} creada` : `${etiqueta} actualizada`,
        );
        onOpenChange(false);
      },
      onError: (error: unknown) =>
        manejarErrorDialogoCatalogo(error, {
          form: form as unknown as import("@/lib/api/apply-server-errors").FormConSetError,
          campoUnicidad: 'clave',
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
      titulo={modo.tipo === 'crear' ? `Nueva ${etiqueta}` : `Editar ${etiqueta}`}
      descripcion="La clave es única en todo el catálogo y es la clave consolidada de reportes."
      formId="dim1-dialog-form"
      onSubmit={form.handleSubmit(onSubmit)}
      submitting={crear.isPending || editar.isPending}
    >
      <Field label="Clave" htmlFor="dim1-clave" required error={form.formState.errors.clave?.message}>
        <Input {...form.register('clave')} id="dim1-clave" maxLength={10} autoFocus />
      </Field>
      <Field label="Nombre" htmlFor="dim1-nombre" full required error={form.formState.errors.nombre?.message}>
        <Input {...form.register('nombre')} id="dim1-nombre" maxLength={254} />
      </Field>
    </CatalogoDialogShell>
  );
}
