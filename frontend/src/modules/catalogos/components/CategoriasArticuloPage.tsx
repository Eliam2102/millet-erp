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
  useActualizarCategoriaArticulo,
  useCategoriasArticuloList,
  useDesactivarCategoriaArticulo,
} from '@/modules/catalogos/api';
import type { CategoriaArticuloResponse } from '@/modules/catalogos/api/types';
import {
  ActualizarCategoriaArticuloSchema,
  type ActualizarCategoriaArticuloValues,
} from '@/modules/catalogos/schemas/categoria-articulo';
import { useFormIdempotencyKey, esApiError, applyServerErrors } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevaCategoriaArticulo } from '@/modules/catalogos/components/nueva-categoria-articulo-context';
import { toast } from 'sonner';

/**
 * Página P1 del catálogo Categorías de Artículo (patrón ADR-0046). Reemplaza
 * el string libre <c>Articulo.Categoria</c>. Mutación con el permiso grueso
 * <c>compartido.catalogos.administrar</c> (molde UsoPrincipal).
 */
export function CategoriasArticuloPage() {
  const canGestionar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );
  const nueva = useNuevaCategoriaArticulo();
  const query = useCategoriasArticuloList();
  const desactivar = useDesactivarCategoriaArticulo();

  const items = useMemo(() => query.data ?? [], [query.data]);

  const columns: ReadonlyArray<CatalogoColumn<CategoriaArticuloResponse>> = [
    { key: 'nombre', label: 'Nombre' },
  ];

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">
          Categorías de artículo
        </h1>
        {canGestionar && (
          <Button size="sm" onClick={() => nueva.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nueva categoría
          </Button>
        )}
      </div>

      <CatalogoEditableTable
        recurso="categorías de artículo"
        recursoSingular="Categoría"
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
          <CategoriaArticuloInlineEditForm item={item} onClose={onClose} />
        )}
        emptyMessage={
          canGestionar
            ? 'Crea la primera para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        emptyAction={
          canGestionar ? (
            <Button size="sm" onClick={() => nueva.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva categoría
            </Button>
          ) : undefined
        }
      />
    </div>
  );
}

function CategoriaArticuloInlineEditForm({
  item,
  onClose,
}: {
  item: CategoriaArticuloResponse;
  onClose: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarCategoriaArticulo();
  const form = useForm<ActualizarCategoriaArticuloValues>({
    resolver: zodResolver(ActualizarCategoriaArticuloSchema),
    defaultValues: { nombre: item.nombre },
  });

  function onSubmit(values: ActualizarCategoriaArticuloValues) {
    actualizar.mutate(
      {
        id: item.id,
        payload: { nombre: values.nombre },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Categoría actualizada');
          onClose();
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'CATEGORIA_ARTICULO_NOMBRE_DUPLICADO') {
              form.setError('nombre', {
                type: err.code,
                message: 'Ya existe una categoría con ese nombre.',
              });
              return;
            }
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
          toast.error('Error al actualizar la categoría.');
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel={`Editar categoría ${item.nombre}`}
      onSubmit={form.handleSubmit(onSubmit)}
      onCancel={onClose}
      isPending={actualizar.isPending}
    >
      <div className="grid grid-cols-1 gap-2">
        <FieldInline
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
        >
          <Input maxLength={100} {...form.register('nombre')} />
        </FieldInline>
      </div>
    </InlineFormShell>
  );
}
