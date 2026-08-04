import { hoyLocalISO } from '@/lib/datetime';
import { useEffect, useState } from 'react';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Building2, Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { SucursalSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { CartaPorteSchema, type CartaPorteValues } from '@/features/facturacion/schemas/carta-porte';
import { useEmitirCartaPorte } from '@/features/facturacion/api/useCartaPorte';
import { useEmisorDefaults } from '@/features/facturacion/api/useFacturas';
import type { EmisorDefaultsResponse } from '@/features/facturacion/api/types';
import { EmisorInfoBar } from '@/features/facturacion/components/EmisorInfoBar';
import {
  ReceptorFiscalInfo,
  ReceptorIncompletoBanner,
} from '@/features/facturacion/components/ReceptorFiscalInfo';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import { VehiculoPicker } from '@/features/facturacion/components/VehiculoPicker';
import { OperadorPicker } from '@/features/facturacion/components/OperadorPicker';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevaCartaPorte/&gt;</c> — emisión de Carta Porte 3.1 (FE-F8).
 * Vehículo y operador se seleccionan de los catálogos administrables
 * (<c>/admin/carta-porte-catalogos</c>) vía pickers.
 *
 * <para>Emisor y receptor son SIEMPRE de solo lectura: el emisor fijo
 * de la empresa y el receptor del master de clientes (entrega a
 * cliente) o copiado del emisor con "Usar datos del emisor" (traslado
 * propio tipo T). Cero captura manual de datos fiscales.</para>
 */
export interface NuevaCartaPorteProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

function defaultMercancia() {
  return {
    descripcion: '',
    bienesTransp: '',
    claveUnidad: 'KGM',
    cantidad: 1,
    pesoEnKg: 1,
    materialPeligroso: false,
  };
}

const VALORES_INICIALES: CartaPorteValues = {
  tipoCfdi: 'T',
  sucursalId: '',
  receptorRfc: '',
  receptorNombre: '',
  receptorRegimenFiscal: '',
  receptorCodigoPostal: '',
  receptorUsoCfdi: 'S01',
  receptorPais: 'MEX',
  rfcEmisor: '',
  regimenFiscalEmisor: '',
  moneda: 'MXN',
  origen: '',
  destino: '',
  origenCodigoPostal: '',
  origenEstado: '',
  destinoCodigoPostal: '',
  destinoEstado: '',
  distanciaKm: 0,
  vehiculoId: '',
  operadorId: '',
  fechaSalida: hoyLocalISO(),
  fechaLlegadaEstimada: hoyLocalISO(),
  montoServicio: 0,
  tasaIvaServicio: 0.16,
  mercancias: [defaultMercancia()],
};

const numeroONull = (v: unknown): number | null => {
  if (v === '' || v == null) return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
};

export function NuevaCartaPorte(props: NuevaCartaPorteProps) {
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
}: NuevaCartaPorteProps & { emisor: EmisorDefaultsResponse }) {
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirCartaPorte();
  // El receptor es de solo lectura; el permiso solo controla el CTA al
  // catálogo de clientes (mismo patrón que la emisión de factura).
  const puedeCorregirEnCatalogo = useHasPermission(
    PermisosCanonicos.FacturacionFacturasEditarReceptor,
  );
  // Cliente elegido para entrega (null cuando el receptor es el propio
  // emisor — traslado T). Solo alimenta el CTA al catálogo.
  const [clienteId, setClienteId] = useState<string | null>(null);

  const form = useForm<CartaPorteValues>({
    resolver: zodResolver(CartaPorteSchema),
    defaultValues: {
      ...VALORES_INICIALES,
      rfcEmisor: emisor.rfcEmisor,
      regimenFiscalEmisor: emisor.regimenFiscalEmisor,
      sucursalId: emisor.sucursalIdDefault ?? '',
    },
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'mercancias',
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const [receptorRfc, receptorNombre, receptorRegimen, receptorCp] = useWatch({
    control: form.control,
    name: [
      'receptorRfc',
      'receptorNombre',
      'receptorRegimenFiscal',
      'receptorCodigoPostal',
    ],
  });
  const receptorBloqueado = [
    receptorRfc,
    receptorNombre,
    receptorRegimen,
    receptorCp,
  ].some((v) => (v ?? '').trim() === '');

  /** Traslado propio (tipo T): receptor = el mismo emisor. */
  function usarDatosDelEmisor() {
    setClienteId(null);
    const opts = { shouldDirty: true } as const;
    form.setValue('receptorRfc', emisor.rfcEmisor, opts);
    form.setValue('receptorNombre', emisor.razonSocialEmisor, opts);
    form.setValue('receptorRegimenFiscal', emisor.regimenFiscalEmisor, opts);
    form.setValue('receptorCodigoPostal', emisor.codigoPostalEmisor ?? '', opts);
    form.setValue('receptorUsoCfdi', 'S01', opts);
  }

  function onSubmit(values: CartaPorteValues) {
    emitir.mutate(
      {
        command: {
          sucursalId: values.sucursalId,
          cajaId: null,
          tipoCfdi: values.tipoCfdi,
          receptorRfc: values.receptorRfc.toUpperCase(),
          receptorNombre: values.receptorNombre,
          receptorRegimenFiscal: values.receptorRegimenFiscal,
          receptorCodigoPostal: values.receptorCodigoPostal,
          receptorUsoCfdi: values.receptorUsoCfdi,
          // Sin campo en el form: la Carta Porte es traslado nacional.
          receptorPais: 'MEX',
          rfcEmisor: values.rfcEmisor.toUpperCase(),
          regimenFiscalEmisor: values.regimenFiscalEmisor,
          moneda: values.moneda.toUpperCase(),
          origen: values.origen,
          destino: values.destino,
          origenCodigoPostal: values.origenCodigoPostal,
          origenEstado: values.origenEstado.toUpperCase(),
          destinoCodigoPostal: values.destinoCodigoPostal,
          destinoEstado: values.destinoEstado.toUpperCase(),
          distanciaKm: values.distanciaKm,
          vehiculoId: values.vehiculoId,
          operadorId: values.operadorId,
          pedidoFacturableId: null,
          fechaSalida: `${values.fechaSalida}T08:00:00Z`,
          fechaLlegadaEstimada: `${values.fechaLlegadaEstimada}T18:00:00Z`,
          montoServicio: values.montoServicio,
          tasaIvaServicio: values.tasaIvaServicio,
          mercancias: values.mercancias,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            res.uuid
              ? `Carta Porte ${res.folio} timbrada (${res.tipoCfdi})`
              : `Carta Porte ${res.folio} emitida (${res.estado})`,
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
          toast.error('Error inesperado al emitir la Carta Porte.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      {/* Emisor fijo (datos de la empresa) — solo informativo. */}
      <EmisorInfoBar emisor={emisor} />

      <Seccion titulo="Tipo y receptor">
        <Campo label="Tipo de CFDI" required>
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('tipoCfdi')}
          >
            <option value="T">T — Traslado (sin ingreso)</option>
            <option value="I">I — Ingreso (flete facturado)</option>
          </select>
        </Campo>
        <Campo label="Sucursal" required error={form.formState.errors.sucursalId?.message}>
          <Controller
            name="sucursalId"
            control={form.control}
            render={({ field }) => (
              <SucursalSelector value={field.value || null} onChange={(id) => field.onChange(id ?? '')} />
            )}
          />
        </Campo>
        <Campo
          label="Cliente (entrega)"
          hint="Autollena el receptor desde el master; para traslado propio usa los datos del emisor."
        >
          <ClienteSelector
            value={clienteId}
            onChange={(item) => {
              setClienteId(item?.id ?? null);
              if (item == null) return;
              const opts = { shouldDirty: true } as const;
              form.setValue('receptorNombre', item.razonSocial, opts);
              form.setValue('receptorRfc', item.rfc ?? '', opts);
              form.setValue('receptorRegimenFiscal', item.regimenFiscal ?? '', opts);
              form.setValue('receptorCodigoPostal', item.codigoPostalFiscal ?? '', opts);
              if (item.usoCfdiDefault) form.setValue('receptorUsoCfdi', item.usoCfdiDefault, opts);
            }}
          />
        </Campo>
        <div className="flex items-end pb-0.5">
          <Button type="button" variant="outline" size="sm" onClick={usarDatosDelEmisor}>
            <Building2 className="mr-1 h-3 w-3" />
            Usar datos del emisor (traslado propio)
          </Button>
        </div>
        <div className="sm:col-span-2">
          <ReceptorFiscalInfo
            valores={{
              rfc: receptorRfc,
              nombre: receptorNombre,
              regimenFiscal: receptorRegimen,
              codigoPostal: receptorCp,
            }}
            clienteId={clienteId}
            puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
          />
        </div>
        {receptorBloqueado && (
          <ReceptorIncompletoBanner
            clienteId={clienteId}
            puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
            textoSinCliente="Selecciona un cliente o usa los datos del emisor para poder emitir."
            className="sm:col-span-2"
          />
        )}
        <Campo label="Uso CFDI" required error={form.formState.errors.receptorUsoCfdi?.message}>
          <Input className="uppercase" maxLength={5} {...form.register('receptorUsoCfdi')} />
        </Campo>
        <Campo label="Moneda" required error={form.formState.errors.moneda?.message}>
          <Input className="uppercase" maxLength={3} {...form.register('moneda')} />
        </Campo>
      </Seccion>

      <Seccion titulo="Tramo, vehículo y operador">
        <Campo label="Origen" required error={form.formState.errors.origen?.message}>
          <Input {...form.register('origen')} />
        </Campo>
        <Campo label="CP origen" required error={form.formState.errors.origenCodigoPostal?.message}>
          <Input className="font-mono" maxLength={5} inputMode="numeric" {...form.register('origenCodigoPostal')} />
        </Campo>
        <Campo
          label="Estado origen (clave SAT)"
          required
          error={form.formState.errors.origenEstado?.message}
          hint="Catálogo SAT c_Estado (QUE, CMX, JAL…)."
        >
          <Input className="uppercase" maxLength={3} {...form.register('origenEstado')} />
        </Campo>
        <Campo label="Destino" required error={form.formState.errors.destino?.message}>
          <Input {...form.register('destino')} />
        </Campo>
        <Campo label="CP destino" required error={form.formState.errors.destinoCodigoPostal?.message}>
          <Input className="font-mono" maxLength={5} inputMode="numeric" {...form.register('destinoCodigoPostal')} />
        </Campo>
        <Campo
          label="Estado destino (clave SAT)"
          required
          error={form.formState.errors.destinoEstado?.message}
          hint="Catálogo SAT c_Estado (QUE, CMX, JAL…)."
        >
          <Input className="uppercase" maxLength={3} {...form.register('destinoEstado')} />
        </Campo>
        <Campo label="Distancia (km)" required error={form.formState.errors.distanciaKm?.message}>
          <Input type="number" step="0.1" min="0" {...form.register('distanciaKm', { valueAsNumber: true })} />
        </Campo>
        <Campo label="Vehículo" required error={form.formState.errors.vehiculoId?.message}>
          <Controller
            name="vehiculoId"
            control={form.control}
            render={({ field }) => (
              <VehiculoPicker value={field.value || null} onChange={(id) => field.onChange(id ?? '')} />
            )}
          />
        </Campo>
        <Campo label="Operador" required error={form.formState.errors.operadorId?.message}>
          <Controller
            name="operadorId"
            control={form.control}
            render={({ field }) => (
              <OperadorPicker value={field.value || null} onChange={(id) => field.onChange(id ?? '')} />
            )}
          />
        </Campo>
        <Campo label="Fecha de salida" required error={form.formState.errors.fechaSalida?.message}>
          <Input type="date" {...form.register('fechaSalida')} />
        </Campo>
        <Campo label="Fecha llegada estimada" required error={form.formState.errors.fechaLlegadaEstimada?.message}>
          <Input type="date" {...form.register('fechaLlegadaEstimada')} />
        </Campo>
        <Campo label="Monto del servicio (tipo I)" error={form.formState.errors.montoServicio?.message} hint="0 para Traslado (T).">
          <Input type="number" step="0.01" min="0" {...form.register('montoServicio', { valueAsNumber: true })} />
        </Campo>
        <Campo label="Tasa IVA servicio" error={form.formState.errors.tasaIvaServicio?.message}>
          <Input type="number" step="0.01" min="0" max="1" {...form.register('tasaIvaServicio', { setValueAs: numeroONull })} />
        </Campo>
      </Seccion>

      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">Mercancías ({fields.length})</h3>
          <Button type="button" variant="ghost" size="sm" onClick={() => append(defaultMercancia())}>
            <Plus className="mr-1 h-3 w-3" />
            Agregar mercancía
          </Button>
        </header>
        <div className="space-y-2">
          {fields.map((field, index) => {
            const errs = form.formState.errors.mercancias?.[index];
            return (
              <div
                key={field.id}
                className={cn(
                  'grid grid-cols-1 gap-2 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3 sm:grid-cols-12',
                  errs && 'border-destructive/50',
                )}
              >
                <div className="sm:col-span-5">
                  <Label className="text-xs">Descripción *</Label>
                  <Input {...form.register(`mercancias.${index}.descripcion` as const)} />
                </div>
                <div className="sm:col-span-3">
                  <Label className="text-xs">Bienes transp. (SAT) *</Label>
                  <Input maxLength={10} {...form.register(`mercancias.${index}.bienesTransp` as const)} />
                </div>
                <div className="sm:col-span-2">
                  <Label className="text-xs">Unidad *</Label>
                  <Input maxLength={10} {...form.register(`mercancias.${index}.claveUnidad` as const)} />
                </div>
                <div className="sm:col-span-1">
                  <Label className="text-xs">Cant. *</Label>
                  <Input type="number" step="0.001" min="0" {...form.register(`mercancias.${index}.cantidad` as const, { valueAsNumber: true })} />
                </div>
                <div className="sm:col-span-1">
                  <Label className="text-xs">Peso kg *</Label>
                  <Input type="number" step="0.001" min="0" {...form.register(`mercancias.${index}.pesoEnKg` as const, { valueAsNumber: true })} />
                </div>
                <div className="flex items-end gap-2 sm:col-span-3">
                  <label className="flex items-center gap-1 text-xs">
                    <input type="checkbox" className="size-4 rounded border-input" {...form.register(`mercancias.${index}.materialPeligroso` as const)} />
                    Material peligroso
                  </label>
                </div>
                <div className="flex items-end justify-end sm:col-span-9">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => remove(index)}
                    disabled={fields.length <= 1}
                    className="text-destructive hover:bg-destructive/10"
                  >
                    <Trash2 className="mr-1 h-3 w-3" />
                    Quitar
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
        {form.formState.errors.mercancias?.message && (
          <p className="text-xs text-destructive">
            {form.formState.errors.mercancias.message}
          </p>
        )}
      </section>

      <div className="flex items-center justify-end gap-2 border-t pt-4">
        <Button type="button" variant="ghost" onClick={() => onClose()} disabled={emitir.isPending}>
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={emitir.isPending || receptorBloqueado}
          title={
            receptorBloqueado
              ? 'Receptor incompleto: elige cliente o usa los datos del emisor'
              : undefined
          }
        >
          {emitir.isPending ? 'Emitiendo…' : 'Emitir Carta Porte'}
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
