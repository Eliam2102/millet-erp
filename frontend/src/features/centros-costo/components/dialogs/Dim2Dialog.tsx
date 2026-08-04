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
import type { Dim2Detalle } from '@/features/centros-costo/api/types';
import { Dim2Schema, type Dim2Values } from '@/features/centros-costo/schemas/catalogo';
import { CatalogoDialogShell } from './CatalogoDialogShell';
import { manejarErrorDialogoCatalogo } from './catalogo-dialog-helpers';
import { GrupoDimCombobox } from '../GrupoDimCombobox';
import { Field } from '../internal/Field';

/**
 * Crear (con PADRE HEREDADO en el título — inmutable, nunca campo) o
 * editar (clave/nombre/grupo — el padre no se toca) una Dimensión 2.
 */
export type Dim2DialogModo =
  | { tipo: 'crear'; padre: { id: string; clave: string; nombre: string } }
  | { tipo: 'editar'; id: string };

interface Dim2DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  modo: Dim2DialogModo;
}

export function Dim2Dialog({ open, onOpenChange, modo }: Dim2DialogProps) {
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const etiqueta = etiquetaNivel('dim2', 'configuracion');
  const etiquetaGrupo = etiquetaNivel('grupoDim2', 'configuracion');

  const detalle = useDetalleCatalogo<Dim2Detalle>(
    'dim2',
    modo.tipo === 'editar' ? modo.id : null,
  );
  const crear = useCrearCatalogo<Dim2Values & { dim1Id: string }>('dim2');
  const editar = useEditarCatalogo<Dim2Values>('dim2');

  const form = useForm<Dim2Values>({
    resolver: zodResolver(Dim2Schema),
    defaultValues: { clave: '', nombre: '', grupoDim2Id: '' },
  });

  useEffect(() => {
    if (!open) return;
    if (modo.tipo === 'editar' && detalle.data) {
      form.reset({
        clave: detalle.data.data.clave,
        nombre: detalle.data.data.nombre,
        grupoDim2Id: detalle.data.data.grupoDim2Id,
      });
    }
    if (modo.tipo === 'crear') {
      form.reset({ clave: '', nombre: '', grupoDim2Id: '' });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, modo.tipo, detalle.data]);

  function onSubmit(values: Dim2Values) {
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
      crear.mutate({ ...values, dim1Id: modo.padre.id }, opciones);
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
      formId="dim2-dialog-form"
      onSubmit={form.handleSubmit(onSubmit)}
      submitting={crear.isPending || editar.isPending}
    >
      <Field label="Clave" htmlFor="dim2-clave" required error={form.formState.errors.clave?.message}>
        <Input {...form.register('clave')} id="dim2-clave" maxLength={20} autoFocus />
      </Field>
      <Field label="Nombre" htmlFor="dim2-nombre" full required error={form.formState.errors.nombre?.message}>
        <Input {...form.register('nombre')} id="dim2-nombre" maxLength={254} />
      </Field>
      <Field
        label={etiquetaGrupo}
        required
        error={form.formState.errors.grupoDim2Id?.message}
      >
        <Controller
          name="grupoDim2Id"
          control={form.control}
          render={({ field }) => (
            <GrupoDimCombobox
              recurso="grupos-dim2"
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
      </Field>
    </CatalogoDialogShell>
  );
}
