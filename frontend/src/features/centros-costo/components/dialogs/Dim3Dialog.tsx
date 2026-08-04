import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
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
import type { Dim3Detalle } from '@/features/centros-costo/api/types';
import { Dim3Schema, type Dim3Values } from '@/features/centros-costo/schemas/catalogo';
import { CatalogoDialogShell } from './CatalogoDialogShell';
import { manejarErrorDialogoCatalogo } from './catalogo-dialog-helpers';
import { GrupoDimCombobox } from '../GrupoDimCombobox';
import { Field } from '../internal/Field';

/**
 * Crear (PADRE HEREDADO en el título) o editar (clave/nombre/grupo —
 * nunca el padre) una Dimensión 3 — la hoja del catálogo.
 */
export type Dim3DialogModo =
  | { tipo: 'crear'; padre: { id: string; clave: string; nombre: string } }
  | { tipo: 'editar'; id: string };

interface Dim3DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  modo: Dim3DialogModo;
}

export function Dim3Dialog({ open, onOpenChange, modo }: Dim3DialogProps) {
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const etiqueta = etiquetaNivel('dim3', 'configuracion');
  const etiquetaGrupo = etiquetaNivel('grupoDim3', 'configuracion');

  const detalle = useDetalleCatalogo<Dim3Detalle>(
    'dim3',
    modo.tipo === 'editar' ? modo.id : null,
  );
  const crear = useCrearCatalogo<Dim3Values & { dim2Id: string }>('dim3');
  const editar = useEditarCatalogo<Dim3Values>('dim3');

  const form = useForm<Dim3Values>({
    resolver: zodResolver(Dim3Schema),
    defaultValues: { clave: '', nombre: '', grupoDim3Id: '' },
  });

  useEffect(() => {
    if (!open) return;
    if (modo.tipo === 'editar' && detalle.data) {
      form.reset({
        clave: detalle.data.data.clave,
        nombre: detalle.data.data.nombre,
        grupoDim3Id: detalle.data.data.grupoDim3Id,
      });
    }
    if (modo.tipo === 'crear') {
      form.reset({ clave: '', nombre: '', grupoDim3Id: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, modo.tipo, detalle.data]);

  function onSubmit(values: Dim3Values) {
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
    if (modo.tipo === 'crear') {
      crear.mutate({ ...values, dim2Id: modo.padre.id }, opciones);
    } else {
      editar.mutate({ id: modo.id, body: values }, opciones);
    }
  }

  const titulo =
    modo.tipo === 'crear'
      ? `Nueva ${etiqueta} en ${modo.padre.clave} — ${modo.padre.nombre}`
      : `Editar ${etiqueta}`;

  return (
    <CatalogoDialogShell
      open={open}
      onOpenChange={onOpenChange}
      titulo={titulo}
      descripcion={
        modo.tipo === 'crear'
          ? `En ${modo.padre.clave} — ${modo.padre.nombre}. El padre queda fijo: reubicar es baja + alta (diseño §5).`
          : 'El padre no se puede cambiar: reubicar es baja + alta (diseño §5).'
      }
      formId="dim3-dialog-form"
      onSubmit={form.handleSubmit(onSubmit)}
      submitting={crear.isPending || editar.isPending}
    >
      <Field label="Clave" htmlFor="dim3-clave" required error={form.formState.errors.clave?.message}>
        <Input {...form.register('clave')} id="dim3-clave" maxLength={20} autoFocus />
      </Field>
      <Field label="Nombre" htmlFor="dim3-nombre" full required error={form.formState.errors.nombre?.message}>
        <Input {...form.register('nombre')} id="dim3-nombre" maxLength={254} />
      </Field>
      <Field
        label={etiquetaGrupo}
        required
        error={form.formState.errors.grupoDim3Id?.message}
      >
        <Controller
          name="grupoDim3Id"
          control={form.control}
          render={({ field }) => (
            <GrupoDimCombobox
              recurso="grupos-dim3"
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
      </Field>
    </CatalogoDialogShell>
  );
}
