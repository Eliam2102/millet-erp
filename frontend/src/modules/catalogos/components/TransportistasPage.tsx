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
  useActualizarTransportista,
  useDesactivarTransportista,
  useTransportistasList,
} from '@/modules/catalogos/api';
import type { TransportistaResponse } from '@/modules/catalogos/api/types';
import {
  ActualizarTransportistaSchema,
  type ActualizarTransportistaValues,
} from '@/modules/catalogos/schemas/transportista';
import { useFormIdempotencyKey, esApiError, applyServerErrors } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevoTransportista } from '@/modules/catalogos/components/nuevo-transportista-context';
import { toast } from 'sonner';

/**
 * Página P1 del catálogo Transportistas (UF-Admin-PR5.2). Email y
 * Telefono son opcionales (limpiar* flags en el PATCH backend).
 */
export function TransportistasPage() {
  const canGestionar = useHasPermission(
    PermisosCanonicos.CatalogosTransportistasGestionar,
  );
  const nuevo = useNuevoTransportista();
  const query = useTransportistasList();
  const desactivar = useDesactivarTransportista();

  const items = useMemo(() => query.data ?? [], [query.data]);

  const columns: ReadonlyArray<CatalogoColumn<TransportistaResponse>> = [
    {
      key: 'clave',
      label: 'Clave',
      render: (i) => <span className="font-mono">{i.clave}</span>,
      className: 'w-32',
    },
    { key: 'nombre', label: 'Nombre' },
    {
      key: 'email',
      label: 'Email',
      render: (i) => (
        <span className="text-muted-foreground">{i.email ?? '—'}</span>
      ),
    },
    {
      key: 'telefono',
      label: 'Teléfono',
      render: (i) => (
        <span className="text-muted-foreground">{i.telefono ?? '—'}</span>
      ),
      className: 'w-40',
    },
  ];

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">Transportistas</h1>
        {canGestionar && (
          <Button size="sm" onClick={() => nuevo.abrir()}>
            <Plus className="mr-1 h-4 w-4" />
            Nuevo transportista
          </Button>
        )}
      </div>

      <CatalogoEditableTable
        recurso="transportistas"
        recursoSingular="Transportista"
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
          <TransportistaInlineEditForm item={item} onClose={onClose} />
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
              Nuevo transportista
            </Button>
          ) : undefined
        }
      />
    </div>
  );
}

interface TransportistaInlineEditFormProps {
  item: TransportistaResponse;
  onClose: () => void;
}

function TransportistaInlineEditForm({
  item,
  onClose,
}: TransportistaInlineEditFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarTransportista();
  const form = useForm<ActualizarTransportistaValues>({
    resolver: zodResolver(ActualizarTransportistaSchema),
    defaultValues: {
      nombre: item.nombre,
      email: item.email,
      telefono: item.telefono,
    },
  });

  function onSubmit(values: ActualizarTransportistaValues) {
    const emailVacio = values.email == null || values.email.length === 0;
    const telVacio = values.telefono == null || values.telefono.length === 0;
    actualizar.mutate(
      {
        id: item.id,
        payload: {
          nombre: values.nombre,
          email: emailVacio ? null : values.email,
          telefono: telVacio ? null : values.telefono,
          limpiarEmail: emailVacio,
          limpiarTelefono: telVacio,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Transportista actualizado');
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
          toast.error('Error al actualizar el transportista.');
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel={`Editar transportista ${item.clave}`}
      onSubmit={form.handleSubmit(onSubmit)}
      onCancel={onClose}
      isPending={actualizar.isPending}
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline label="Clave" className="md:col-span-2">
          <Input value={item.clave} readOnly className="font-mono" />
        </FieldInline>
        <FieldInline
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-4"
        >
          <Input maxLength={254} {...form.register('nombre')} />
        </FieldInline>
        <FieldInline
          label="Email"
          error={form.formState.errors.email?.message}
          className="md:col-span-3"
        >
          <Controller
            name="email"
            control={form.control}
            render={({ field }) => (
              <Input
                type="email"
                maxLength={254}
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(
                    e.target.value.length > 0 ? e.target.value : null,
                  )
                }
              />
            )}
          />
        </FieldInline>
        <FieldInline
          label="Teléfono"
          error={form.formState.errors.telefono?.message}
          className="md:col-span-3"
        >
          <Controller
            name="telefono"
            control={form.control}
            render={({ field }) => (
              <Input
                maxLength={50}
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(
                    e.target.value.length > 0 ? e.target.value : null,
                  )
                }
              />
            )}
          />
        </FieldInline>
      </div>
    </InlineFormShell>
  );
}
