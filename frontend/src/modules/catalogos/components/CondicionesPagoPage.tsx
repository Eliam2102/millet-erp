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
  useActualizarCondicionesPago,
  useCondicionesPagoList,
  useDesactivarCondicionesPago,
} from '@/modules/catalogos/api';
import type { CondicionesPagoResponse } from '@/modules/catalogos/api/types';
import {
  ActualizarCondicionesPagoSchema,
  type ActualizarCondicionesPagoValues,
} from '@/modules/catalogos/schemas/condiciones-pago';
import { useFormIdempotencyKey, esApiError, applyServerErrors } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevaCondicionesPago } from '@/modules/catalogos/components/nueva-condiciones-pago-context';
import { toast } from 'sonner';

/**
 * Página P1 (bandeja tabular full-width) del catálogo Condiciones de
 * Pago (UF-Admin-PR5.2). Edición inline (border-amber); alta vía Sheet.
 */
export function CondicionesPagoPage() {
  const canGestionar = useHasPermission(
    PermisosCanonicos.CatalogosCondicionesPagoGestionar,
  );
  const nueva = useNuevaCondicionesPago();
  const query = useCondicionesPagoList();
  const desactivar = useDesactivarCondicionesPago();

  const items = useMemo(() => query.data ?? [], [query.data]);

  const columns: ReadonlyArray<CatalogoColumn<CondicionesPagoResponse>> = [
    {
      key: 'clave',
      label: 'Clave',
      render: (i) => <span className="font-mono">{i.clave}</span>,
      className: 'w-32',
    },
    { key: 'nombre', label: 'Nombre' },
    {
      key: 'diasCredito',
      label: 'Días crédito',
      render: (i) => <span className="font-mono">{i.diasCredito}</span>,
      className: 'w-32',
    },
  ];

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">
          Condiciones de pago
        </h1>
        {canGestionar && (
          <Button size="sm" onClick={() => nueva.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nueva condición
          </Button>
        )}
      </div>

      <CatalogoEditableTable
        recurso="condiciones de pago"
        recursoSingular="Condición de pago"
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
          <CondicionesPagoInlineEditForm item={item} onClose={onClose} />
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
              Nueva condición
            </Button>
          ) : undefined
        }
      />
    </div>
  );
}

interface CondicionesPagoInlineEditFormProps {
  item: CondicionesPagoResponse;
  onClose: () => void;
}

function CondicionesPagoInlineEditForm({
  item,
  onClose,
}: CondicionesPagoInlineEditFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarCondicionesPago();
  const form = useForm<ActualizarCondicionesPagoValues>({
    resolver: zodResolver(ActualizarCondicionesPagoSchema),
    defaultValues: {
      nombre: item.nombre,
      diasCredito: item.diasCredito,
    },
  });

  function onSubmit(values: ActualizarCondicionesPagoValues) {
    actualizar.mutate(
      {
        id: item.id,
        payload: { nombre: values.nombre, diasCredito: values.diasCredito },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Condición de pago actualizada');
          onClose();
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                err,
              )
            ) {
              return;
            }
            toast.error(err.problem.title);
            return;
          }
          toast.error('Error al actualizar la condición.');
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel={`Editar condición ${item.clave}`}
      onSubmit={form.handleSubmit(onSubmit)}
      onCancel={onClose}
      isPending={actualizar.isPending}
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline
          label="Clave"
          className="md:col-span-3"
        >
          <Input value={item.clave} readOnly className="font-mono" />
        </FieldInline>
        <FieldInline
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-6"
        >
          <Input maxLength={100} {...form.register('nombre')} />
        </FieldInline>
        <FieldInline
          label="Días crédito"
          required
          error={form.formState.errors.diasCredito?.message}
          className="md:col-span-3"
        >
          <Input
            type="number"
            min={0}
            max={365}
            step={1}
            {...form.register('diasCredito', { valueAsNumber: true })}
          />
        </FieldInline>
      </div>
    </InlineFormShell>
  );
}
