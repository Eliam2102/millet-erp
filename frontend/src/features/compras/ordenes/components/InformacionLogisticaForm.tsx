import { useState } from 'react';
import { Pencil, Save, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  TextAreaField,
  TransportistaSelector,
} from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  accionEditarInfoLogistica,
} from '@/features/compras/ordenes/lib/acciones-disponibles';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';
import {
  useActualizarInformacionLogistica,
  type ActualizarInformacionLogisticaCommand,
} from '@/features/compras/ordenes/api/useActualizarInformacionLogistica';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { useTransportistas } from '@/features/catalogos/api';
import { cn } from '@/lib/utils';
import { useForm, Controller } from 'react-hook-form';

/**
 * <c>&lt;InformacionLogisticaForm/&gt;</c> — sub-tab "Logística" del
 * Tab "Información" del detalle de OC. Read-only por default; click
 * "Editar" expande inline (border ámbar) — mismo patrón que
 * <c>LineaInlineFormOc</c> en edit mode.
 *
 * <para>Editable hasta <c>Autorizada</c> inclusive (doc 05 §4.6 — la
 * info logística cambia durante el ciclo de recepción sin re-auth).</para>
 *
 * <para>Transportista: el selector usa el catálogo F9-PR1; si el
 * transportista no está catalogado, el input <c>transportistaTexto</c>
 * actúa como fallback (los dos pueden coexistir; el comprador suele
 * usar uno u otro).</para>
 */
export interface InformacionLogisticaFormProps {
  oc: OrdenCompraDetalleResponse;
}

interface LogisticaValues {
  direccionEntrega: string;
  transportistaId: string;
  transportistaTexto: string;
  numeroGuia: string;
  instruccionesEnvio: string;
}

export function InformacionLogisticaForm({ oc }: InformacionLogisticaFormProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const accion = accionEditarInfoLogistica(oc, permisos);
  const transportistasQuery = useTransportistas();
  const [editing, setEditing] = useState(false);

  const transportistaResolved = oc.infoLogisticaTransportistaId
    ? transportistasQuery.data?.find(
        (t) => t.id === oc.infoLogisticaTransportistaId,
      )
    : undefined;

  if (!editing) {
    return (
      <ReadView
        oc={oc}
        transportistaResolved={transportistaResolved}
        canEdit={accion.visible && accion.habilitada}
        onEditar={() => setEditing(true)}
      />
    );
  }

  return (
    <EditView
      oc={oc}
      onCancel={() => setEditing(false)}
      onSaved={() => setEditing(false)}
    />
  );
}

// ─── Read mode ────────────────────────────────────────────────────

function ReadView({
  oc,
  transportistaResolved,
  canEdit,
  onEditar,
}: {
  oc: OrdenCompraDetalleResponse;
  transportistaResolved:
    | { clave: string; nombre: string }
    | undefined;
  canEdit: boolean;
  onEditar: () => void;
}) {
  const transportistaLabel = transportistaResolved
    ? `${transportistaResolved.clave} · ${transportistaResolved.nombre}`
    : oc.infoLogisticaTransportistaTexto ?? null;

  return (
    <div
      className="rounded-md border bg-card p-4 space-y-3"
      data-component="info-logistica-read"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">
          Información logística
        </h3>
        {canEdit && (
          <Button
            size="sm"
            variant="outline"
            onClick={onEditar}
            data-action="editar-info-logistica"
          >
            <Pencil className="mr-1 h-3.5 w-3.5" />
            Editar
          </Button>
        )}
      </div>

      <dl className="grid gap-3 text-sm md:grid-cols-2">
        <Field label="Dirección de entrega">
          {oc.infoLogisticaDireccion ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Transportista">
          {transportistaLabel ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Número de guía">
          {oc.infoLogisticaNumeroGuia ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Instrucciones de envío" className="md:col-span-2">
          {oc.infoLogisticaInstrucciones ?? <span className="text-muted-foreground">—</span>}
        </Field>
      </dl>
    </div>
  );
}

// ─── Edit mode ────────────────────────────────────────────────────

function EditView({
  oc,
  onCancel,
  onSaved,
}: {
  oc: OrdenCompraDetalleResponse;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const mutation = useActualizarInformacionLogistica();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  const form = useForm<LogisticaValues>({
    defaultValues: {
      direccionEntrega: oc.infoLogisticaDireccion ?? '',
      transportistaId: oc.infoLogisticaTransportistaId ?? '',
      transportistaTexto: oc.infoLogisticaTransportistaTexto ?? '',
      numeroGuia: oc.infoLogisticaNumeroGuia ?? '',
      instruccionesEnvio: oc.infoLogisticaInstrucciones ?? '',
    },
  });

  function onSubmit(values: LogisticaValues) {
    const command: ActualizarInformacionLogisticaCommand = {
      direccionEntrega: nullIfEmpty(values.direccionEntrega),
      transportistaId: nullIfEmpty(values.transportistaId),
      transportistaTexto: nullIfEmpty(values.transportistaTexto),
      numeroGuia: nullIfEmpty(values.numeroGuia),
      instruccionesEnvio: nullIfEmpty(values.instruccionesEnvio),
    };
    mutation.mutate(
      { ordenCompraId: oc.id, command, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Información logística actualizada.');
          onSaved();
        },
        onError: (err) => {
          // 409 → ConflictDialog en modo simple. Cierra el form para
          // que el dialog se vea sin overlap.
          if (
            handleOcMutationError(err, {
              ordenCompraId: oc.id,
              conflictDialog,
              queryClient,
            })
          ) {
            onCancel();
            return;
          }
          if (esApiError(err)) {
            applyServerErrors(
              form as unknown as Parameters<typeof applyServerErrors>[0],
              err,
            );
            toast.error(err.problem.title, {
              description: err.traceId
                ? `Código: ${err.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al guardar la logística.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !mutation.isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn(
        'space-y-3 rounded-md border p-3',
        'border-amber-400 bg-amber-50/40',
      )}
      aria-label="Editar información logística"
      data-component="info-logistica-edit"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">
          Información logística
        </h3>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={onCancel}
            disabled={mutation.isPending}
          >
            <X className="mr-1 h-4 w-4" />
            Cancelar
          </Button>
          <Button
            type="submit"
            size="sm"
            disabled={mutation.isPending}
            data-action="submit-info-logistica"
          >
            <Save className="mr-1 h-4 w-4" />
            {mutation.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      </div>

      <FieldGroup label="Dirección de entrega">
        <Controller
          name="direccionEntrega"
          control={form.control}
          render={({ field }) => (
            <TextAreaField
              value={field.value || null}
              onChange={(v) => field.onChange(v ?? '')}
              maxLength={500}
              minRows={2}
              textareaProps={{
                placeholder: 'Calle, número, colonia, ciudad, CP…',
              }}
            />
          )}
        />
      </FieldGroup>

      <div className="grid gap-3 md:grid-cols-2">
        <FieldGroup label="Transportista (catálogo)">
          <Controller
            name="transportistaId"
            control={form.control}
            render={({ field }) => (
              <TransportistaSelector
                value={field.value || null}
                onChange={(v) => field.onChange(v ?? '')}
              />
            )}
          />
        </FieldGroup>
        <FieldGroup label="Transportista (texto libre, fallback)">
          <Input
            type="text"
            maxLength={120}
            placeholder="Si no está en el catálogo"
            {...form.register('transportistaTexto')}
          />
        </FieldGroup>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <FieldGroup label="Número de guía">
          <Input
            type="text"
            maxLength={60}
            {...form.register('numeroGuia')}
          />
        </FieldGroup>
        <FieldGroup label="Instrucciones de envío">
          <Controller
            name="instruccionesEnvio"
            control={form.control}
            render={({ field }) => (
              <TextAreaField
                value={field.value || null}
                onChange={(v) => field.onChange(v ?? '')}
                maxLength={500}
                minRows={2}
              />
            )}
          />
        </FieldGroup>
      </div>
    </form>
  );
}

// ─── Helpers ──────────────────────────────────────────────────────

function nullIfEmpty(v: string): string | null {
  const trimmed = v.trim();
  return trimmed === '' ? null : trimmed;
}

function Field({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className={cn('space-y-0.5', className)}>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

function FieldGroup({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-medium text-muted-foreground">
        {label}
      </label>
      {children}
    </div>
  );
}
