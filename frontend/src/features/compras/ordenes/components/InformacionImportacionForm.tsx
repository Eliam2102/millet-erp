import { useState } from 'react';
import { Pencil, Save, X, Info } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { IncotermSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { accionEditarInfoImportacion } from '@/features/compras/ordenes/lib/acciones-disponibles';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';
import {
  useActualizarInformacionImportacion,
  type ActualizarInformacionImportacionCommand,
} from '@/features/compras/ordenes/api/useActualizarInformacionImportacion';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { useIncoterms } from '@/features/catalogos/api';
import { cn } from '@/lib/utils';
import { useForm, Controller } from 'react-hook-form';

/**
 * <c>&lt;InformacionImportacionForm/&gt;</c> — sub-tab "Importación"
 * del Tab "Información" del detalle de OC. Solo se renderiza si
 * <c>oc.esImportacion === true</c> (caso contrario el padre debe
 * ocultar el sub-tab).
 *
 * <para>Editable solo en <c>Borrador</c>/<c>Rechazada</c>. El campo
 * <c>NumeroPedimento</c> tiene su propio endpoint dedicado
 * (<c>PATCH /numero-pedimento</c>) editable post-autorización sin
 * re-auth — este form lo edita junto con el resto en
 * Borrador/Rechazada; UF4 cableará la edición post-aut con un mini-form
 * separado.</para>
 */
export interface InformacionImportacionFormProps {
  oc: OrdenCompraDetalleResponse;
}

interface ImportacionValues {
  incotermId: string;
  paisOrigen: string;
  numeroContenedor: string;
  codigoRuta: string;
  semanaEmbarque: string;
  numeroPedimento: string;
}

export function InformacionImportacionForm({
  oc,
}: InformacionImportacionFormProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const accion = accionEditarInfoImportacion(oc, permisos);
  const incotermsQuery = useIncoterms();
  const [editing, setEditing] = useState(false);

  if (!oc.esImportacion) {
    return null;
  }

  const incotermResolved = oc.infoImportIncotermId
    ? incotermsQuery.data?.find((i) => i.id === oc.infoImportIncotermId)
    : undefined;

  if (!editing) {
    return (
      <ReadView
        oc={oc}
        incotermResolved={incotermResolved}
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
  incotermResolved,
  canEdit,
  onEditar,
}: {
  oc: OrdenCompraDetalleResponse;
  incotermResolved: { codigo: string; nombre: string } | undefined;
  canEdit: boolean;
  onEditar: () => void;
}) {
  const incotermLabel = incotermResolved
    ? `${incotermResolved.codigo} · ${incotermResolved.nombre}`
    : null;

  return (
    <div
      className="rounded-md border bg-card p-4 space-y-3"
      data-component="info-importacion-read"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">
          Información de importación
        </h3>
        {canEdit && (
          <Button
            size="sm"
            variant="outline"
            onClick={onEditar}
            data-action="editar-info-importacion"
          >
            <Pencil className="mr-1 h-3.5 w-3.5" />
            Editar
          </Button>
        )}
      </div>

      <dl className="grid gap-3 text-sm md:grid-cols-2">
        <Field label="Incoterm">
          {incotermLabel ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="País de origen">
          {oc.infoImportPaisOrigen ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Número de contenedor">
          {oc.infoImportNumeroContenedor ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Código de ruta">
          {oc.infoImportCodigoRuta ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Semana de embarque">
          {oc.infoImportSemanaEmbarque ?? <span className="text-muted-foreground">—</span>}
        </Field>
        <Field label="Número de pedimento">
          {oc.infoImportNumeroPedimento ?? <span className="text-muted-foreground">—</span>}
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
  const mutation = useActualizarInformacionImportacion();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  const form = useForm<ImportacionValues>({
    defaultValues: {
      incotermId: oc.infoImportIncotermId ?? '',
      paisOrigen: oc.infoImportPaisOrigen ?? '',
      numeroContenedor: oc.infoImportNumeroContenedor ?? '',
      codigoRuta: oc.infoImportCodigoRuta ?? '',
      semanaEmbarque: oc.infoImportSemanaEmbarque ?? '',
      numeroPedimento: oc.infoImportNumeroPedimento ?? '',
    },
  });

  function onSubmit(values: ImportacionValues) {
    const command: ActualizarInformacionImportacionCommand = {
      incotermId: nullIfEmpty(values.incotermId),
      paisOrigen: nullIfEmpty(values.paisOrigen),
      numeroContenedor: nullIfEmpty(values.numeroContenedor),
      codigoRuta: nullIfEmpty(values.codigoRuta),
      semanaEmbarque: nullIfEmpty(values.semanaEmbarque),
      numeroPedimento: nullIfEmpty(values.numeroPedimento),
    };
    mutation.mutate(
      { ordenCompraId: oc.id, command, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Información de importación actualizada.');
          onSaved();
        },
        onError: (err) => {
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
          toast.error('Error inesperado al guardar la importación.');
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
      aria-label="Editar información de importación"
      data-component="info-importacion-edit"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">
          Información de importación
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
            data-action="submit-info-importacion"
          >
            <Save className="mr-1 h-4 w-4" />
            {mutation.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <FieldGroup label="Incoterm">
          <Controller
            name="incotermId"
            control={form.control}
            render={({ field }) => (
              <IncotermSelector
                value={field.value || null}
                onChange={(v) => field.onChange(v ?? '')}
              />
            )}
          />
        </FieldGroup>
        <FieldGroup label="País de origen">
          <Input
            type="text"
            maxLength={80}
            placeholder="MX, US, CN…"
            {...form.register('paisOrigen')}
          />
        </FieldGroup>
        <FieldGroup label="Número de contenedor">
          <Input
            type="text"
            maxLength={20}
            placeholder="ABCU1234567"
            {...form.register('numeroContenedor')}
          />
        </FieldGroup>
        <FieldGroup label="Código de ruta">
          <Input
            type="text"
            maxLength={40}
            {...form.register('codigoRuta')}
          />
        </FieldGroup>
        <FieldGroup label="Semana de embarque">
          <Input
            type="text"
            maxLength={20}
            placeholder="2026-W12"
            {...form.register('semanaEmbarque')}
          />
        </FieldGroup>
        <FieldGroup label="Número de pedimento">
          <Input
            type="text"
            maxLength={40}
            {...form.register('numeroPedimento')}
          />
        </FieldGroup>
      </div>

      <p className="flex items-start gap-2 text-xs text-muted-foreground">
        <Info className="h-3.5 w-3.5 mt-0.5 shrink-0" />
        <span>
          Tip: <strong>Número de pedimento</strong> es editable hasta el
          cierre de la OC. Después de la autorización, UF4 ofrecerá un
          mini-form dedicado para ajustarlo sin re-firmar.
        </span>
      </p>
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
