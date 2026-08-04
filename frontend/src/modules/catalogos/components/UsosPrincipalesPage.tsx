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
  useActualizarUsoPrincipal,
  useDesactivarUsoPrincipal,
  useUsosPrincipalesList,
} from '@/modules/catalogos/api';
import type { UsoPrincipalResponse } from '@/modules/catalogos/api/types';
import {
  ActualizarUsoPrincipalSchema,
  type ActualizarUsoPrincipalValues,
} from '@/modules/catalogos/schemas/uso-principal';
import { useFormIdempotencyKey, esApiError, applyServerErrors } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevoUsoPrincipal } from '@/modules/catalogos/components/nuevo-uso-principal-context';
import { toast } from 'sonner';

/**
 * Página P1 del catálogo Usos Principales (UF-Admin-PR5.2). Backend
 * actualmente reusa el permiso grueso
 * <c>compartido.catalogos.administrar</c> para mutación.
 */
export function UsosPrincipalesPage() {
  // Usamos el grueso porque el backend de UsosPrincipales aún no
  // expone un granular. Si se agrega después, sustituir aquí.
  const canGestionar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );
  const nuevo = useNuevoUsoPrincipal();
  const query = useUsosPrincipalesList();
  const desactivar = useDesactivarUsoPrincipal();

  const items = useMemo(() => query.data ?? [], [query.data]);

  const columns: ReadonlyArray<CatalogoColumn<UsoPrincipalResponse>> = [
    {
      key: 'clave',
      label: 'Clave',
      render: (i) => <span className="font-mono">{i.clave}</span>,
      className: 'w-32',
    },
    { key: 'nombre', label: 'Nombre' },
  ];

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">
          Usos principales
        </h1>
        {canGestionar && (
          <Button size="sm" onClick={() => nuevo.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nuevo uso
          </Button>
        )}
      </div>

      <CatalogoEditableTable
        recurso="usos principales"
        recursoSingular="Uso principal"
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
          <UsoPrincipalInlineEditForm item={item} onClose={onClose} />
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
              Nuevo uso
            </Button>
          ) : undefined
        }
      />
    </div>
  );
}

function UsoPrincipalInlineEditForm({
  item,
  onClose,
}: {
  item: UsoPrincipalResponse;
  onClose: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarUsoPrincipal();
  const form = useForm<ActualizarUsoPrincipalValues>({
    resolver: zodResolver(ActualizarUsoPrincipalSchema),
    defaultValues: { nombre: item.nombre },
  });

  function onSubmit(values: ActualizarUsoPrincipalValues) {
    actualizar.mutate(
      {
        id: item.id,
        payload: { nombre: values.nombre },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Uso principal actualizado');
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
          toast.error('Error al actualizar el uso principal.');
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel={`Editar uso principal ${item.clave}`}
      onSubmit={form.handleSubmit(onSubmit)}
      onCancel={onClose}
      isPending={actualizar.isPending}
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline label="Clave" className="md:col-span-3">
          <Input value={item.clave} readOnly className="font-mono" />
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
