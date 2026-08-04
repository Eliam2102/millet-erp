import { useMemo } from 'react';
import { Plus } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  CatalogoEditableTable,
  type CatalogoColumn,
} from '@/modules/catalogos/components/CatalogoEditableTable';
import {
  FieldInline,
  InlineFormShell,
} from '@/modules/catalogos/components/InlineFormShell';
import {
  useActualizarIncoterm,
  useDesactivarIncoterm,
  useIncotermsList,
} from '@/modules/catalogos/api';
import type { IncotermResponse } from '@/modules/catalogos/api/types';
import {
  ActualizarIncotermSchema,
  type ActualizarIncotermValues,
} from '@/modules/catalogos/schemas/incoterm';
import { useFormIdempotencyKey, esApiError, applyServerErrors } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevoIncoterm } from '@/modules/catalogos/components/nuevo-incoterm-context';
import { toast } from 'sonner';

/**
 * Página P1 del catálogo Incoterms (UF-Admin-PR5.2). Codigo es
 * inmutable; el inline form solo permite renombrar.
 */
export function IncotermsPage() {
  const canGestionar = useHasPermission(
    PermisosCanonicos.CatalogosIncotermsGestionar,
  );
  const nuevo = useNuevoIncoterm();
  const query = useIncotermsList();
  const desactivar = useDesactivarIncoterm();

  const items = useMemo(() => query.data ?? [], [query.data]);

  const columns: ReadonlyArray<CatalogoColumn<IncotermResponse>> = [
    {
      key: 'codigo',
      label: 'Código',
      render: (i) => <span className="font-mono">{i.codigo}</span>,
      className: 'w-32',
    },
    { key: 'nombre', label: 'Nombre' },
  ];

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">Incoterms</h1>
        {canGestionar && (
          <Button size="sm" onClick={() => nuevo.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nuevo Incoterm
          </Button>
        )}
      </div>

      <CatalogoEditableTable
        recurso="Incoterms"
        recursoSingular="Incoterm"
        items={items}
        isLoading={query.isLoading}
        isError={query.isError}
        error={query.error}
        onRetry={() => query.refetch()}
        columns={columns}
        canEditar={canGestionar}
        canDesactivar={canGestionar}
        desactivar={desactivar}
        renderInlineEditForm={(item, onClose) => (
          <IncotermInlineEditForm item={item} onClose={onClose} />
        )}
        emptyMessage={
          canGestionar
            ? 'Crea el primero para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        emptyAction={
          canGestionar ? (
            <Button size="sm" onClick={() => nuevo.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo Incoterm
            </Button>
          ) : undefined
        }
      />
    </div>
  );
}

interface IncotermInlineEditFormProps {
  item: IncotermResponse;
  onClose: () => void;
}

function IncotermInlineEditForm({ item, onClose }: IncotermInlineEditFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarIncoterm();
  const form = useForm<ActualizarIncotermValues>({
    resolver: zodResolver(ActualizarIncotermSchema),
    defaultValues: { nombre: item.nombre },
  });

  function onSubmit(values: ActualizarIncotermValues) {
    actualizar.mutate(
      {
        id: item.id,
        payload: { nombre: values.nombre },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Incoterm actualizado');
          onClose();
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                err,
              )
            )
              return;
            toast.error(err.problem.title);
            return;
          }
          toast.error('Error al actualizar el Incoterm.');
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel={`Editar Incoterm ${item.codigo}`}
      onSubmit={form.handleSubmit(onSubmit)}
      onCancel={onClose}
      isPending={actualizar.isPending}
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline label="Código" className="md:col-span-3">
          <Input value={item.codigo} readOnly className="font-mono" />
        </FieldInline>
        <FieldInline
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-9"
        >
          <Input maxLength={100} {...form.register('nombre')} />
        </FieldInline>
      </div>
    </InlineFormShell>
  );
}
