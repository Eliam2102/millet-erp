import { useMemo } from 'react';
import { Plus } from 'lucide-react';
import { Controller, useForm } from 'react-hook-form';
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
  useActualizarUnidadMedida,
  useDesactivarUnidadMedida,
  useUnidadesMedidaList,
  type UnidadMedidaRow,
} from '@/modules/catalogos/api';
import {
  DimensionUnidad,
  DIMENSION_UNIDAD_LABEL,
  type ActualizarUnidadMedidaPayload,
} from '@/modules/catalogos/api/types';
import {
  ActualizarUnidadMedidaSchema,
  type ActualizarUnidadMedidaValues,
} from '@/modules/catalogos/schemas/unidad-medida';
import { useFormIdempotencyKey, esApiError, applyServerErrors } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevaUnidadMedida } from '@/modules/catalogos/components/nueva-unidad-medida-context';
import { DimensionSelect } from '@/modules/catalogos/components/unidad-medida-fields';
import { toast } from 'sonner';

/**
 * Página del catálogo de unidades de medida (ADR-0046 Etapa 1a). Código
 * inmutable. Nombre y decimales se editan libremente; dimensión y factor son
 * editables hoy (nadie referencia la unidad aún), pero el backend los bloquea
 * cuando la unidad esté en uso (Etapa 1b).
 */
export function UnidadesMedidaPage() {
  const canGestionar = useHasPermission(
    PermisosCanonicos.CatalogosUnidadesMedidaGestionar,
  );
  const nuevo = useNuevaUnidadMedida();
  const query = useUnidadesMedidaList();
  const desactivar = useDesactivarUnidadMedida();

  const items = useMemo(() => query.data ?? [], [query.data]);

  const columns: ReadonlyArray<CatalogoColumn<UnidadMedidaRow>> = [
    {
      key: 'codigo',
      label: 'Código',
      render: (i) => <span className="font-mono">{i.codigo}</span>,
      className: 'w-24',
    },
    { key: 'nombre', label: 'Nombre' },
    {
      key: 'dimension',
      label: 'Dimensión',
      render: (i) => DIMENSION_UNIDAD_LABEL[i.dimension],
    },
    {
      key: 'factorABase',
      label: 'Factor a base',
      render: (i) => <span className="tabular-nums">{i.factorABase}</span>,
      className: 'text-right',
    },
    {
      key: 'decimales',
      label: 'Decimales',
      render: (i) => <span className="tabular-nums">{i.decimales}</span>,
      className: 'text-right',
    },
    {
      key: 'esBase',
      label: 'Base',
      render: (i) => (i.esBase ? 'Sí' : '—'),
      className: 'w-16',
    },
  ];

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">
          Unidades de medida
        </h1>
        {canGestionar && (
          <Button size="sm" onClick={() => nuevo.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nueva unidad
          </Button>
        )}
      </div>

      <CatalogoEditableTable
        recurso="unidades de medida"
        recursoSingular="Unidad de medida"
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
          <UnidadMedidaInlineEditForm item={item} onClose={onClose} />
        )}
        emptyMessage={
          canGestionar
            ? 'Crea la primera para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        emptyAction={
          canGestionar ? (
            <Button size="sm" onClick={() => nuevo.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva unidad
            </Button>
          ) : undefined
        }
      />
    </div>
  );
}

interface UnidadMedidaInlineEditFormProps {
  item: UnidadMedidaRow;
  onClose: () => void;
}

function UnidadMedidaInlineEditForm({
  item,
  onClose,
}: UnidadMedidaInlineEditFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarUnidadMedida();
  const form = useForm<ActualizarUnidadMedidaValues>({
    resolver: zodResolver(ActualizarUnidadMedidaSchema),
    defaultValues: {
      nombre: item.nombre,
      dimension: item.dimension,
      factorABase: item.factorABase,
      decimales: item.decimales,
      esBase: item.esBase,
    },
  });

  function onSubmit(values: ActualizarUnidadMedidaValues) {
    const payload: ActualizarUnidadMedidaPayload = {
      nombre: values.nombre,
      decimales: values.decimales,
      dimension: values.dimension as DimensionUnidad,
      factorABase: values.factorABase,
      esBase: values.esBase,
    };
    actualizar.mutate(
      { id: item.id, payload, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Unidad de medida actualizada');
          onClose();
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'UNIDAD_MEDIDA_EN_USO') {
              toast.error(
                'No se puede cambiar la dimensión o el factor: la unidad ya está en uso.',
              );
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
          toast.error('Error al actualizar la unidad de medida.');
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel={`Editar unidad ${item.codigo}`}
      onSubmit={form.handleSubmit(onSubmit)}
      onCancel={onClose}
      isPending={actualizar.isPending}
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline label="Código" className="md:col-span-2">
          <Input value={item.codigo} readOnly className="font-mono" />
        </FieldInline>
        <FieldInline
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-4"
        >
          <Input maxLength={100} {...form.register('nombre')} />
        </FieldInline>
        <FieldInline
          label="Dimensión"
          required
          hint="Bloqueada una vez en uso."
          error={form.formState.errors.dimension?.message}
          className="md:col-span-3"
        >
          <Controller
            name="dimension"
            control={form.control}
            render={({ field }) => (
              <DimensionSelect value={field.value} onChange={field.onChange} />
            )}
          />
        </FieldInline>
        <FieldInline
          label="Factor a base"
          required
          error={form.formState.errors.factorABase?.message}
          className="md:col-span-3"
        >
          <Input
            type="number"
            inputMode="decimal"
            step="any"
            min={0}
            {...form.register('factorABase', { valueAsNumber: true })}
          />
        </FieldInline>
        <FieldInline
          label="Decimales"
          required
          error={form.formState.errors.decimales?.message}
          className="md:col-span-2"
        >
          <Input
            type="number"
            inputMode="numeric"
            step={1}
            min={0}
            max={6}
            {...form.register('decimales', { valueAsNumber: true })}
          />
        </FieldInline>
        <FieldInline label="Es base" className="md:col-span-3">
          <Controller
            name="esBase"
            control={form.control}
            render={({ field }) => (
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border-input"
                  checked={field.value}
                  onChange={(e) => field.onChange(e.target.checked)}
                />
                <span>{field.value ? 'Base' : 'Derivada'}</span>
              </label>
            )}
          />
        </FieldInline>
      </div>
    </InlineFormShell>
  );
}
