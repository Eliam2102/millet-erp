import { useState } from 'react';
import { Pencil, Save, X, RotateCcw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { DecimalField } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { accionEditarCabecera } from '@/features/compras/ordenes/lib/acciones-disponibles';
import {
  DescuentoTipo,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import {
  useActualizarTotalesFinancieros,
  type ActualizarTotalesFinancierosCommand,
} from '@/features/compras/ordenes/api/useActualizarTotalesFinancieros';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { cn } from '@/lib/utils';
import { useForm, Controller, useWatch } from 'react-hook-form';

/**
 * <c>&lt;TotalesFinancierosForm/&gt;</c> — sub-tab "Financiera" del
 * Tab "Información" del detalle de OC. Muestra los totales calculados
 * sobre las líneas + permite editar los 4 modificadores de cabecera:
 *
 * <list>
 *   <item><b>Descuento global</b>: tipo (porcentaje/monto) + valor.</item>
 *   <item><b>Gastos adicionales</b>: monto fijo (envío, comisiones).</item>
 *   <item><b>Redondeo</b>: positivo o negativo, ajuste de cierre.</item>
 * </list>
 *
 * <para>Editable solo en <c>Borrador</c>/<c>Rechazada</c> (vía
 * <c>accionEditarCabecera</c>). Tras guardar, el backend recalcula
 * impuestos y devuelve el detalle actualizado.</para>
 *
 * <para><b>Subtotal de líneas</b> y <b>IVA total</b> se calculan
 * client-side desde <c>oc.lineas</c> para preview inmediato; el
 * <b>Total a pagar</b> es <c>subtotal + IVA - descuento global +
 * gastos + redondeo</c> (mismo criterio del motor backend).</para>
 */
export interface TotalesFinancierosFormProps {
  oc: OrdenCompraDetalleResponse;
}

interface TotalesValues {
  descuentoGlobalTipo: DescuentoTipo | null;
  descuentoGlobalValor: number | null;
  gastosAdicionales: number;
  redondeo: number;
}

export function TotalesFinancierosForm({ oc }: TotalesFinancierosFormProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const accion = accionEditarCabecera(oc, permisos);
  const [editing, setEditing] = useState(false);

  const subtotalLineas = oc.lineas.reduce((acc, l) => acc + l.subtotalLinea, 0);
  const ivaTotal = oc.lineas.reduce((acc, l) => acc + l.ivaImporte, 0);
  const descuentoGlobalAplicado = computeDescuentoGlobal(
    subtotalLineas,
    oc.descuentoGlobalTipo,
    oc.descuentoGlobalValor,
  );
  const totalAPagar =
    subtotalLineas +
    ivaTotal -
    descuentoGlobalAplicado +
    oc.gastosAdicionales +
    oc.redondeo;

  if (!editing) {
    return (
      <ReadView
        oc={oc}
        subtotalLineas={subtotalLineas}
        ivaTotal={ivaTotal}
        descuentoGlobalAplicado={descuentoGlobalAplicado}
        totalAPagar={totalAPagar}
        canEdit={accion.visible && accion.habilitada}
        onEditar={() => setEditing(true)}
      />
    );
  }

  return (
    <EditView
      oc={oc}
      subtotalLineas={subtotalLineas}
      ivaTotal={ivaTotal}
      onCancel={() => setEditing(false)}
      onSaved={() => setEditing(false)}
    />
  );
}

// ─── Read mode ────────────────────────────────────────────────────

function ReadView({
  oc,
  subtotalLineas,
  ivaTotal,
  descuentoGlobalAplicado,
  totalAPagar,
  canEdit,
  onEditar,
}: {
  oc: OrdenCompraDetalleResponse;
  subtotalLineas: number;
  ivaTotal: number;
  descuentoGlobalAplicado: number;
  totalAPagar: number;
  canEdit: boolean;
  onEditar: () => void;
}) {
  return (
    <div
      className="rounded-md border bg-card p-4 space-y-3"
      data-component="totales-financieros-read"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">
          Totales financieros
        </h3>
        {canEdit && (
          <Button
            size="sm"
            variant="outline"
            onClick={onEditar}
            data-action="editar-totales-financieros"
          >
            <Pencil className="mr-1 h-3.5 w-3.5" />
            Editar
          </Button>
        )}
      </div>

      <dl className="grid gap-2 text-sm">
        <Row label="Subtotal de líneas" value={subtotalLineas} />
        <Row label="IVA (16%)" value={ivaTotal} muted />
        <Row
          label={
            oc.descuentoGlobalTipo == null
              ? 'Descuento global'
              : oc.descuentoGlobalTipo === DescuentoTipo.Porcentaje
                ? `Descuento global (${(oc.descuentoGlobalValor ?? 0).toFixed(2)}%)`
                : 'Descuento global (monto)'
          }
          value={-descuentoGlobalAplicado}
          showSign
          muted={descuentoGlobalAplicado === 0}
        />
        <Row
          label="Gastos adicionales"
          value={oc.gastosAdicionales}
          showSign
          muted={oc.gastosAdicionales === 0}
        />
        <Row
          label="Redondeo"
          value={oc.redondeo}
          showSign
          muted={oc.redondeo === 0}
        />
        <div className="border-t pt-2">
          <Row label="Total a pagar" value={totalAPagar} bold />
          <p className="mt-1 text-xs text-muted-foreground">
            Moneda: {oc.moneda}
            {oc.tipoCambio != null
              ? ` (TC: ${oc.tipoCambio.toFixed(4)})`
              : ''}
          </p>
        </div>
      </dl>
    </div>
  );
}

// ─── Edit mode ────────────────────────────────────────────────────

function EditView({
  oc,
  subtotalLineas,
  ivaTotal,
  onCancel,
  onSaved,
}: {
  oc: OrdenCompraDetalleResponse;
  subtotalLineas: number;
  ivaTotal: number;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const mutation = useActualizarTotalesFinancieros();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  const form = useForm<TotalesValues>({
    defaultValues: {
      descuentoGlobalTipo: oc.descuentoGlobalTipo,
      descuentoGlobalValor: oc.descuentoGlobalValor,
      gastosAdicionales: oc.gastosAdicionales,
      redondeo: oc.redondeo,
    },
  });

  function onSubmit(values: TotalesValues) {
    const command: ActualizarTotalesFinancierosCommand = {
      gastosAdicionales: values.gastosAdicionales,
      redondeo: values.redondeo,
    };
    if (values.descuentoGlobalTipo == null) {
      command.limpiarDescuentoGlobal = true;
    } else {
      command.descuentoGlobalTipo = values.descuentoGlobalTipo;
      command.descuentoGlobalValor = values.descuentoGlobalValor ?? 0;
    }
    mutation.mutate(
      { ordenCompraId: oc.id, command, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Totales financieros actualizados.');
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
          toast.error('Error inesperado al guardar los totales.');
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
      aria-label="Editar totales financieros"
      data-component="totales-financieros-edit"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">
          Totales financieros
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
            data-action="submit-totales-financieros"
          >
            <Save className="mr-1 h-4 w-4" />
            {mutation.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-3">
        <FieldGroup label="Descuento global (tipo)">
          <Controller
            name="descuentoGlobalTipo"
            control={form.control}
            render={({ field }) => (
              <Select
                value={
                  field.value == null ? '__none__' : String(field.value)
                }
                onValueChange={(v) =>
                  field.onChange(v === '__none__' ? null : Number(v))
                }
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__none__">Sin descuento</SelectItem>
                  <SelectItem value={String(DescuentoTipo.Porcentaje)}>
                    Porcentaje (%)
                  </SelectItem>
                  <SelectItem value={String(DescuentoTipo.Monto)}>
                    Monto fijo
                  </SelectItem>
                </SelectContent>
              </Select>
            )}
          />
        </FieldGroup>
        <FieldGroup label="Valor del descuento">
          <DescuentoValorField control={form.control} />
        </FieldGroup>
        <FieldGroup label="Gastos adicionales">
          <Controller
            name="gastosAdicionales"
            control={form.control}
            render={({ field }) => (
              <DecimalField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v ?? 0)}
                min={0}
                step={0.01}
              />
            )}
          />
        </FieldGroup>
      </div>

      <div className="grid gap-3 md:grid-cols-3">
        <FieldGroup label="Redondeo (positivo o negativo)">
          <Controller
            name="redondeo"
            control={form.control}
            render={({ field }) => (
              <DecimalField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v ?? 0)}
                step={0.01}
              />
            )}
          />
        </FieldGroup>
        <FieldGroup label="Subtotal de líneas (read-only)">
          <ReadOnlyMoney value={subtotalLineas} />
        </FieldGroup>
        <FieldGroup label="IVA total (read-only)">
          <ReadOnlyMoney value={ivaTotal} />
        </FieldGroup>
      </div>

      <p className="flex items-start gap-2 text-xs text-muted-foreground">
        <RotateCcw className="h-3.5 w-3.5 mt-0.5 shrink-0" />
        <span>
          Tras guardar, el backend recalcula impuestos por línea y devuelve
          el detalle actualizado. El total a pagar de arriba se reactualiza
          automáticamente.
        </span>
      </p>
    </form>
  );
}

// ─── Helpers ──────────────────────────────────────────────────────

function computeDescuentoGlobal(
  subtotal: number,
  tipo: DescuentoTipo | null,
  valor: number | null,
): number {
  if (tipo == null || valor == null) return 0;
  if (tipo === DescuentoTipo.Porcentaje) {
    return (subtotal * valor) / 100;
  }
  return Math.min(valor, subtotal);
}

function DescuentoValorField({
  control,
}: {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  control: any;
}) {
  const tipo = useWatch({ control, name: 'descuentoGlobalTipo' });
  return (
    <Controller
      name="descuentoGlobalValor"
      control={control}
      render={({ field }) => (
        <DecimalField
          value={field.value ?? null}
          onChange={(v) => field.onChange(v)}
          min={0}
          step={0.01}
          disabled={tipo == null}
        />
      )}
    />
  );
}

function Row({
  label,
  value,
  showSign,
  muted,
  bold,
}: {
  label: string;
  value: number;
  showSign?: boolean;
  muted?: boolean;
  bold?: boolean;
}) {
  const formatted =
    showSign && value > 0
      ? `+${value.toFixed(2)}`
      : showSign && value < 0
        ? value.toFixed(2)
        : value.toFixed(2);
  return (
    <div
      className={cn(
        'flex items-baseline justify-between gap-3',
        muted && 'text-muted-foreground',
        bold && 'font-semibold',
      )}
    >
      <dt>{label}</dt>
      <dd className="tabular-nums">{formatted}</dd>
    </div>
  );
}

function ReadOnlyMoney({ value }: { value: number }) {
  return (
    <div className="rounded-md border bg-muted/30 px-3 py-1.5 text-sm tabular-nums">
      {value.toFixed(2)}
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
