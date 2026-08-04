import { useEffect } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { SucursalSelector } from '@/components/erp';
import { EmisorInfoBar } from '@/features/facturacion/components/EmisorInfoBar';
import {
  ReceptorFiscalInfo,
  ReceptorIncompletoBanner,
} from '@/features/facturacion/components/ReceptorFiscalInfo';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { AnticipoSchema, type AnticipoValues } from '@/features/facturacion/schemas/anticipo';
import { useEmitirAnticipo } from '@/features/facturacion/api/useAnticipos';
import { useEmisorDefaults } from '@/features/facturacion/api/useFacturas';
import {
  METODOS_PAGO,
  TipoAnticipo,
  type EmisorDefaultsResponse,
} from '@/features/facturacion/api/types';

/**
 * <c>&lt;NuevoAnticipo/&gt;</c> — emisión de factura de anticipo (serie
 * FANT, FE-F4). El anticipo es nominal (no admite genérico).
 *
 * <para>Emisor y receptor son SIEMPRE de solo lectura (mismo patrón que
 * la emisión de factura): el emisor fijo de la empresa
 * (<c>&lt;EmisorInfoBar/&gt;</c>) y el receptor fijo del master de
 * clientes (<c>&lt;ReceptorFiscalInfo/&gt;</c>, autollenado por el
 * <c>&lt;ClienteSelector/&gt;</c>). Cliente incompleto (gap G12) →
 * bloqueo con CTA al catálogo gated por permiso.</para>
 */
export interface NuevoAnticipoProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

const VALORES_INICIALES: AnticipoValues = {
  sucursalId: '',
  clienteId: '',
  receptorRfc: '',
  receptorNombre: '',
  receptorRegimenFiscal: '',
  receptorCodigoPostal: '',
  receptorUsoCfdi: 'G03',
  receptorPais: 'MEX',
  rfcEmisor: '',
  regimenFiscalEmisor: '',
  metodoPago: 'PUE',
  formaPago: '01',
  moneda: 'MXN',
  tipoCambio: null,
  tipoAnticipo: TipoAnticipo.ClientesMxp,
  montoBase: 0,
  tasaIvaTraslado: 0.16,
  descripcion: null,
  obraNombre: null,
};

function nullIfEmpty(v: string | null | undefined): string | null {
  const s = (v ?? '').trim();
  return s.length > 0 ? s : null;
}

const numeroONull = (v: unknown): number | null => {
  if (v === '' || v == null) return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
};

export function NuevoAnticipo(props: NuevoAnticipoProps) {
  // Gate: los defaults del emisor deben existir ANTES de inicializar el
  // useForm (defaultValues se evalúa una sola vez por montaje).
  const defaults = useEmisorDefaults();

  if (defaults.isError) {
    return (
      <div className="space-y-2 rounded-md border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm">
        <p>No se pudieron cargar los datos del emisor.</p>
        <Button type="button" variant="outline" size="sm" onClick={() => defaults.refetch()}>
          Reintentar
        </Button>
      </div>
    );
  }
  if (defaults.isLoading || defaults.data == null) {
    return (
      <div className="space-y-3">
        <div className="h-9 w-72 animate-pulse rounded bg-muted" />
        <div className="h-64 w-full animate-pulse rounded bg-muted" />
      </div>
    );
  }
  return <FormInner {...props} emisor={defaults.data} />;
}

function FormInner({
  onClose,
  onDirtyChange,
  emisor,
}: NuevoAnticipoProps & { emisor: EmisorDefaultsResponse }) {
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirAnticipo();
  // El receptor es de solo lectura; el permiso solo controla el CTA al
  // catálogo de clientes (mismo patrón que la emisión de factura).
  const puedeCorregirEnCatalogo = useHasPermission(
    PermisosCanonicos.FacturacionFacturasEditarReceptor,
  );

  const form = useForm<AnticipoValues>({
    resolver: zodResolver(AnticipoSchema),
    defaultValues: {
      ...VALORES_INICIALES,
      rfcEmisor: emisor.rfcEmisor,
      regimenFiscalEmisor: emisor.regimenFiscalEmisor,
      sucursalId: emisor.sucursalIdDefault ?? '',
      tasaIvaTraslado: emisor.tasaIvaDefault ?? VALORES_INICIALES.tasaIvaTraslado,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const [clienteId, receptorRfc, receptorNombre, receptorRegimen, receptorCp] =
    useWatch({
      control: form.control,
      name: [
        'clienteId',
        'receptorRfc',
        'receptorNombre',
        'receptorRegimenFiscal',
        'receptorCodigoPostal',
      ],
    });
  // Receptor incompleto en el master → se corrige en Datos Maestros, no
  // aquí: la emisión del anticipo queda bloqueada.
  const receptorBloqueado = [
    receptorRfc,
    receptorNombre,
    receptorRegimen,
    receptorCp,
  ].some((v) => (v ?? '').trim() === '');

  function onSubmit(values: AnticipoValues) {
    emitir.mutate(
      {
        command: {
          sucursalId: values.sucursalId,
          clienteId: values.clienteId,
          receptorRfc: values.receptorRfc.toUpperCase(),
          receptorNombre: values.receptorNombre,
          receptorRegimenFiscal: values.receptorRegimenFiscal,
          receptorCodigoPostal: values.receptorCodigoPostal,
          receptorUsoCfdi: values.receptorUsoCfdi,
          // Sin campo en el form: el anticipo es nominal nacional.
          receptorPais: 'MEX',
          rfcEmisor: values.rfcEmisor.toUpperCase(),
          regimenFiscalEmisor: values.regimenFiscalEmisor,
          metodoPago: values.metodoPago,
          formaPago: values.formaPago,
          moneda: values.moneda.toUpperCase(),
          tipoCambio: values.tipoCambio,
          tipoAnticipo: values.tipoAnticipo,
          montoBase: values.montoBase,
          tasaIvaTraslado: values.tasaIvaTraslado,
          descripcion: nullIfEmpty(values.descripcion),
          pedidoFacturableId: null,
          pedidoOrigenRef: null,
          obraId: null,
          obraNombre: nullIfEmpty(values.obraNombre),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            res.uuid
              ? `Anticipo ${res.folio} timbrado · saldo ${res.saldo.toFixed(2)}`
              : `Anticipo ${res.folio} emitido (${res.estado})`,
          );
          onClose({ force: true });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description:
                error.problem.detail ??
                (error.traceId ? `Código: ${error.traceId}` : undefined),
            });
            return;
          }
          toast.error('Error inesperado al emitir el anticipo.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      {/* Emisor fijo (datos de la empresa) — solo informativo. */}
      <EmisorInfoBar emisor={emisor} />

      <Seccion titulo="Receptor (nominal)">
        <Campo label="Cliente" required error={form.formState.errors.clienteId?.message}>
          <Controller
            name="clienteId"
            control={form.control}
            render={({ field }) => (
              <ClienteSelector
                value={field.value || null}
                onChange={(item) => {
                  field.onChange(item?.id ?? '');
                  if (item == null) return;
                  const opts = { shouldDirty: true } as const;
                  form.setValue('receptorNombre', item.razonSocial, opts);
                  form.setValue('receptorRfc', item.rfc ?? '', opts);
                  form.setValue('receptorRegimenFiscal', item.regimenFiscal ?? '', opts);
                  form.setValue('receptorCodigoPostal', item.codigoPostalFiscal ?? '', opts);
                  if (item.usoCfdiDefault) form.setValue('receptorUsoCfdi', item.usoCfdiDefault, opts);
                  if (item.metodoPagoDefault) form.setValue('metodoPago', item.metodoPagoDefault, opts);
                  if (item.formaPagoDefault) form.setValue('formaPago', item.formaPagoDefault, opts);
                }}
              />
            )}
          />
        </Campo>
        <div className="sm:col-span-2">
          <ReceptorFiscalInfo
            valores={{
              rfc: receptorRfc,
              nombre: receptorNombre,
              regimenFiscal: receptorRegimen,
              codigoPostal: receptorCp,
            }}
            clienteId={clienteId || null}
            puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
          />
        </div>
        {receptorBloqueado && (
          <ReceptorIncompletoBanner
            clienteId={clienteId || null}
            puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
            className="sm:col-span-2"
          />
        )}
        <Campo label="Uso CFDI" required error={form.formState.errors.receptorUsoCfdi?.message}>
          <Input className="uppercase" maxLength={5} {...form.register('receptorUsoCfdi')} />
        </Campo>
      </Seccion>

      <Seccion titulo="Anticipo y pago">
        <Campo label="Sucursal" required error={form.formState.errors.sucursalId?.message}>
          <Controller
            name="sucursalId"
            control={form.control}
            render={({ field }) => (
              <SucursalSelector value={field.value || null} onChange={(id) => field.onChange(id ?? '')} />
            )}
          />
        </Campo>
        <Campo label="Tipo de anticipo" required>
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('tipoAnticipo', { valueAsNumber: true })}
          >
            <option value={TipoAnticipo.ClientesMxp}>Clientes MXP</option>
            <option value={TipoAnticipo.ClientesUsd}>Clientes USD</option>
          </select>
        </Campo>
        <Campo label="Monto base" required error={form.formState.errors.montoBase?.message}>
          <Input type="number" step="0.01" min="0" {...form.register('montoBase', { valueAsNumber: true })} />
        </Campo>
        <Campo label="Tasa IVA" error={form.formState.errors.tasaIvaTraslado?.message}>
          <Input
            type="number"
            step="0.01"
            min="0"
            max="1"
            placeholder="0.16"
            {...form.register('tasaIvaTraslado', { setValueAs: numeroONull })}
          />
        </Campo>
        <Campo label="Método de pago" required>
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('metodoPago')}
          >
            {METODOS_PAGO.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </Campo>
        <Campo label="Forma de pago" required error={form.formState.errors.formaPago?.message} hint="Clave SAT c_FormaPago (01, 03, 04…).">
          <Input maxLength={5} {...form.register('formaPago')} />
        </Campo>
        <Campo label="Moneda" required error={form.formState.errors.moneda?.message}>
          <Input className="uppercase" maxLength={3} {...form.register('moneda')} />
        </Campo>
        <Campo label="Tipo de cambio" error={form.formState.errors.tipoCambio?.message} hint="Solo si no es MXN.">
          <Input type="number" step="0.0001" min="0" {...form.register('tipoCambio', { setValueAs: numeroONull })} />
        </Campo>
        <div className="sm:col-span-2">
          <Campo label="Descripción" error={form.formState.errors.descripcion?.message}>
            <Input placeholder="Opcional" maxLength={1000} {...form.register('descripcion')} />
          </Campo>
        </div>
      </Seccion>

      <div className="flex items-center justify-end gap-2 border-t pt-4">
        <Button type="button" variant="ghost" onClick={() => onClose()} disabled={emitir.isPending}>
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={emitir.isPending || receptorBloqueado}
          title={
            receptorBloqueado
              ? 'Receptor incompleto: complétalo en Datos Maestros → Clientes'
              : undefined
          }
        >
          {emitir.isPending ? 'Emitiendo…' : 'Emitir anticipo'}
        </Button>
      </div>
    </form>
  );
}

function Seccion({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-medium">{titulo}</h3>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>
    </section>
  );
}

function Campo({
  label,
  required,
  error,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {hint != null && <p className="text-[11px] leading-tight text-muted-foreground">{hint}</p>}
      {error != null && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
