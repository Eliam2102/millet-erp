import { Controller, useWatch, type UseFormReturn } from 'react-hook-form';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  SucursalSelector,
  UsoCfdiSelector,
  FormaPagoSelector,
} from '@/components/erp';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import {
  METODOS_PAGO,
  type ClienteLookupItem,
  type EmisorDefaultsResponse,
} from '@/features/facturacion/api/types';
import { OPCIONES_COMPORTAMIENTO_FISCAL } from '@/features/facturacion/lib/glosario';
import type { EmitirFacturaValues } from '@/features/facturacion/schemas/emitir-factura';
import { AutorizacionPicker } from '@/features/facturacion/components/AutorizacionPicker';
import { EmisorInfoBar } from '@/features/facturacion/components/EmisorInfoBar';
import {
  ReceptorFiscalInfo,
  ReceptorIncompletoBanner,
} from '@/features/facturacion/components/ReceptorFiscalInfo';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import { CanalVentaSelector } from '@/features/facturacion/components/selectors/CanalVentaSelector';
import { CceIncotermSelector } from '@/features/facturacion/components/selectors/CceIncotermSelector';
import { ClaveSatSelector } from '@/components/erp/selectors/ClaveSatSelector';
import { Campo, Seccion } from './campos';
import { numeroONull } from './valores';

/**
 * Pestaña "Encabezado" del form de emisión (FAC-UX-PR2): receptor +
 * pago/generales + CCE (solo exportación) + autorización de venta de
 * activo fijo. FAC-UX-PR4: el receptor se elige con
 * <c>&lt;ClienteSelector/&gt;</c> y la autorización de activo con
 * <c>&lt;AutorizacionPicker/&gt;</c> — cero GUIDs capturados a mano.
 * Emisor y receptor son SIEMPRE de solo lectura: el emisor fijo de la
 * empresa (<c>&lt;EmisorInfoBar/&gt;</c>) y el receptor fijo del master
 * de clientes (<c>&lt;ReceptorFiscalResumen/&gt;</c>); lo único que se
 * corrige es el catálogo, vía el CTA gated por permiso.
 */
export interface TabEncabezadoProps {
  form: UseFormReturn<EmitirFacturaValues>;
  /** Datos fiscales del emisor (empresa) — informativos, no editables. */
  emisor: EmisorDefaultsResponse;
  /**
   * Permiso `facturacion.facturas.editar-receptor`: muestra el CTA al
   * catálogo de clientes para corregir los datos fiscales del receptor.
   * Los datos son de solo lectura para todos.
   */
  puedeCorregirEnCatalogo: boolean;
  /** Receptor incompleto en el master — la emisión se bloquea. */
  receptorBloqueado: boolean;
  esCce: boolean;
  esActivo: boolean;
  autorizacionId: string;
  onAutorizacionIdChange: (value: string) => void;
  clienteId: string | null;
  onClienteChange: (item: ClienteLookupItem | null) => void;
  /** Etiqueta del cliente precargado (flujo desde pedido). */
  clienteLabel?: string;
  /** True cuando el cliente viene del pedido — no se cambia en la factura. */
  clienteFijo?: boolean;
  /** Nombre del canal precargado desde el pedido (FAC-ING-PR3) — conserva
   * la opción en el selector si el canal ya no está activo. */
  canalVentaNombre?: string;
}

export function TabEncabezado({
  form,
  emisor,
  puedeCorregirEnCatalogo,
  receptorBloqueado,
  esCce,
  esActivo,
  autorizacionId,
  onAutorizacionIdChange,
  clienteId,
  onClienteChange,
  clienteLabel,
  clienteFijo,
  canalVentaNombre,
}: TabEncabezadoProps) {
  const moneda = useWatch({ control: form.control, name: 'moneda' });
  const metodoPago = useWatch({ control: form.control, name: 'metodoPago' });
  const [receptorRfc, receptorNombre, receptorRegimen, receptorCp] = useWatch({
    control: form.control,
    name: [
      'receptorRfc',
      'receptorNombre',
      'receptorRegimenFiscal',
      'receptorCodigoPostal',
    ],
  });
  return (
    <div className="space-y-5">
      {esActivo && (
        <div className="space-y-2 rounded-md border border-amber-300 bg-amber-50/60 px-3 py-2">
          <p className="text-sm">
            Venta de activo fijo: requiere autorización del Contador General.
          </p>
          <div className="sm:w-96">
            <Label className="text-xs">Autorización *</Label>
            <AutorizacionPicker
              value={autorizacionId || null}
              onChange={(id) => onAutorizacionIdChange(id ?? '')}
            />
          </div>
        </div>
      )}

      {/* ── Emisor (fijo, datos de la empresa) ───────────────────── */}
      <EmisorInfoBar emisor={emisor} />

      {/* ── Receptor ─────────────────────────────────────────────── */}
      <Seccion titulo="Receptor">
        <div className="sm:col-span-2">
          <Campo
            label="Cliente"
            hint={
              clienteFijo
                ? 'Cliente del pedido — fijo en esta factura.'
                : 'Selecciónalo del catálogo; sus datos fiscales vienen del master de clientes.'
            }
          >
            <ClienteSelector
              value={clienteId}
              onChange={onClienteChange}
              initialLabel={clienteLabel}
              disabled={clienteFijo}
            />
          </Campo>
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
            className="sm:col-span-2"
          />
        )}
        <Campo
          label="Uso CFDI"
          required
          error={form.formState.errors.receptorUsoCfdi?.message}
          hint="Catálogo SAT c_UsoCFDI."
        >
          <Controller
            name="receptorUsoCfdi"
            control={form.control}
            render={({ field }) => (
              <UsoCfdiSelector
                value={field.value || null}
                onChange={(c) => field.onChange(c ?? '')}
              />
            )}
          />
        </Campo>
      </Seccion>

      {/* ── Pago + generales ─────────────────────────────────────── */}
      <Seccion titulo="Pago y datos generales">
        <Campo label="Sucursal" required error={form.formState.errors.sucursalId?.message}>
          <Controller
            name="sucursalId"
            control={form.control}
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo label="Método de pago" required>
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('metodoPago', {
              // Matriz SAT (CFDI40105): PPD fija la forma en 99 "Por
              // definir"; al volver a PUE se exige una forma real.
              onChange: (e: React.ChangeEvent<HTMLSelectElement>) => {
                const metodo = e.target.value;
                const forma = form.getValues('formaPago');
                if (metodo === 'PPD' && forma !== '99') {
                  form.setValue('formaPago', '99', { shouldValidate: true });
                } else if (metodo === 'PUE' && forma === '99') {
                  form.setValue('formaPago', '01', { shouldValidate: true });
                }
              },
            })}
          >
            {METODOS_PAGO.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </Campo>
        <Campo
          label="Forma de pago"
          required
          error={form.formState.errors.formaPago?.message}
          hint={
            metodoPago === 'PPD'
              ? 'Fijada en 99 — Por definir: el SAT la exige con método PPD (la forma real viaja en el REPP de cada pago).'
              : 'Catálogo SAT c_FormaPago.'
          }
        >
          <Controller
            name="formaPago"
            control={form.control}
            render={({ field }) => (
              <FormaPagoSelector
                value={field.value || null}
                onChange={(c) => field.onChange(c ?? '')}
                disabled={metodoPago === 'PPD'}
              />
            )}
          />
        </Campo>
        <Campo
          label="Moneda"
          required
          error={form.formState.errors.moneda?.message}
        >
          <Controller
            name="moneda"
            control={form.control}
            render={({ field }) => (
              <MonedaSelector
                value={field.value || null}
                onChange={(c) => field.onChange(c ?? '')}
              />
            )}
          />
        </Campo>
        {(moneda ?? '').toUpperCase() !== 'MXN' && (
          <Campo
            label="Tipo de cambio"
            error={form.formState.errors.tipoCambio?.message}
            hint="Requerido cuando la moneda no es MXN."
          >
            <Input
              type="number"
              step="0.0001"
              min="0"
              {...form.register('tipoCambio', { setValueAs: numeroONull })}
            />
          </Campo>
        )}
        <Campo
          label="Canal de venta"
          required
          error={form.formState.errors.canalVenta?.message}
        >
          <Controller
            name="canalVenta"
            control={form.control}
            render={({ field }) => (
              <CanalVentaSelector
                value={field.value ?? null}
                onChange={field.onChange}
                etiquetaValorActual={canalVentaNombre}
              />
            )}
          />
        </Campo>
        <Campo label="Comportamiento fiscal" required>
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('comportamientoFiscal', { valueAsNumber: true })}
          >
            {OPCIONES_COMPORTAMIENTO_FISCAL.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </Campo>
        <div className="sm:col-span-2">
          <Campo label="Obra (nombre)" error={form.formState.errors.obraNombre?.message}>
            <Input placeholder="Opcional" {...form.register('obraNombre')} />
          </Campo>
        </div>
      </Seccion>

      {/* ── Comercio Exterior (CCE) — solo exportación ───────────── */}
      {esCce && (
        <Seccion titulo="Comercio Exterior (CCE)">
          <Campo label="Tipo de operación">
            <Input maxLength={2} {...form.register('cceTipoOperacion')} />
          </Campo>
          <Campo label="Incoterm" hint="Clave SAT c_INCOTERM (FOB, CIF…).">
            <Controller
              control={form.control}
              name="cceIncoterm"
              render={({ field }) => (
                <CceIncotermSelector
                  value={field.value}
                  onChange={field.onChange}
                />
              )}
            />
          </Campo>
          <Campo label="Tipo de cambio DOF">
            <Input
              type="number"
              step="0.0001"
              min="0"
              {...form.register('cceTcDof', { setValueAs: numeroONull })}
            />
          </Campo>
          <Campo label="Núm. reg. id. trib. receptor" hint="Tax ID extranjero del receptor.">
            <Input maxLength={40} {...form.register('cceReceptorNumRegIdTrib')} />
          </Campo>
          <Campo label="País residencia receptor" hint="Clave SAT c_Pais (USA, CAN…).">
            <Controller
              control={form.control}
              name="cceReceptorPaisResidencia"
              render={({ field }) => (
                <ClaveSatSelector
                  catalogo="pais"
                  value={field.value}
                  initialLabel={field.value}
                  placeholder="Buscar país…"
                  onChange={(item) => field.onChange(item?.codigo ?? null)}
                />
              )}
            />
          </Campo>
          <Campo
            label="Clave de pedimento"
            hint="Catálogo SAT c_ClavePedimento (ej. A1). Opcional."
          >
            <Controller
              control={form.control}
              name="cceClaveDePedimento"
              render={({ field }) => (
                <ClaveSatSelector
                  catalogo="clave-pedimento"
                  value={field.value}
                  initialLabel={field.value}
                  placeholder="Buscar pedimento…"
                  onChange={(item) => field.onChange(item?.codigo ?? null)}
                />
              )}
            />
          </Campo>
          <div className="flex items-end pb-1">
            <label className="flex items-center gap-1.5 text-xs">
              <input
                type="checkbox"
                className="size-4 rounded border-input"
                {...form.register('cceCertificadoOrigen')}
              />
              Certificado de origen
            </label>
          </div>
          {/* Domicilio del receptor extranjero (CCE 2.0, F12-PR3): estado y
              CP los exige el SAT para timbrar. */}
          <p className="text-xs font-medium text-muted-foreground sm:col-span-2">
            Domicilio del receptor extranjero
          </p>
          <Campo
            label="Calle"
            error={form.formState.errors.cceReceptorDomicilioCalle?.message}
            hint="Opcional."
          >
            <Input maxLength={200} {...form.register('cceReceptorDomicilioCalle')} />
          </Campo>
          <Campo
            label="Estado / provincia"
            required
            error={form.formState.errors.cceReceptorDomicilioEstado?.message}
            hint="Texto libre del país del receptor (ej. Texas)."
          >
            <Input maxLength={30} {...form.register('cceReceptorDomicilioEstado')} />
          </Campo>
          <Campo
            label="Código postal"
            required
            error={form.formState.errors.cceReceptorDomicilioCodigoPostal?.message}
          >
            <Input maxLength={12} {...form.register('cceReceptorDomicilioCodigoPostal')} />
          </Campo>
        </Seccion>
      )}
    </div>
  );
}

