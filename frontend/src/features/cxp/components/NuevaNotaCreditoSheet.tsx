import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Upload } from 'lucide-react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CapturarNotaCreditoSchema,
  type CapturarNotaCreditoValues,
} from '@/features/cxp/schemas/notas-y-anticipos';
import { useCapturarNotaCredito } from '@/features/cxp/api/useNotasYAnticipos';
import {
  fetchCfdiDetalle,
  fetchCfdiParseado,
} from '@/features/cxp/api/useCfdis';
import {
  TipoCfdi,
  TipoNotaCredito,
  TipoNotaCreditoLabels,
  TipoRelacionCfdi,
  type CfdiListItem,
  type CfdiParseado,
} from '@/features/cxp/api/types';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { fetchProveedoresPorRfc } from '@/features/catalogos/api';
import { ProveedorSelector } from '@/components/erp';

/** TipoRelacion del catálogo SAT c_TipoRelacion → enum del backend. */
const TIPO_RELACION_POR_CODIGO: Record<string, TipoRelacionCfdi> = {
  '01': TipoRelacionCfdi.NotaCredito,
  '03': TipoRelacionCfdi.Devolucion,
  '07': TipoRelacionCfdi.AmortizacionAnticipo,
};

const TIPO_NC_POR_RELACION: Record<TipoRelacionCfdi, TipoNotaCredito> = {
  [TipoRelacionCfdi.NotaCredito]: TipoNotaCredito.Descuento,
  [TipoRelacionCfdi.Devolucion]: TipoNotaCredito.Devolucion,
  [TipoRelacionCfdi.AmortizacionAnticipo]: TipoNotaCredito.AmortizacionAnticipo,
};

/**
 * <c>&lt;NuevaNotaCreditoSheet/&gt;</c> — captura de NC del proveedor.
 * El backend resuelve automáticamente la factura origen vía
 * <c>UuidRelacionCfdi</c>; si no encuentra match queda en EnEspera y
 * el worker la vincula cuando entra la factura.
 *
 * <para>Patrón "remount on open" con <c>key={open}</c> para evitar
 * <c>setState</c> en effect.</para>
 */
export interface NuevaNotaCreditoSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaNotaCreditoSheet({
  open,
  onOpenChange,
}: NuevaNotaCreditoSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Capturar nota de crédito</SheetTitle>
          <SheetDescription>
            Captura una NC del proveedor (CFDI 01/03/07). La factura origen
            se vincula automáticamente por UUID relacionado.
          </SheetDescription>
        </SheetHeader>
        {open && <NuevaNcForm onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function NuevaNcForm({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const capturar = useCapturarNotaCredito();
  const hoy = hoyLocalISO();

  const [cfdiSel, setCfdiSel] = useState<CfdiListItem | null>(null);
  const [cargarCfdiOpen, setCargarCfdiOpen] = useState(false);

  const form = useForm<CapturarNotaCreditoValues>({
    resolver: zodResolver(CapturarNotaCreditoSchema),
    defaultValues: {
      cfdiRecibidoId: null,
      uuidCfdi: '',
      proveedorId: '',
      folioProveedor: null,
      serieProveedor: null,
      fechaCfdi: hoy,
      moneda: 'MXN',
      tipoCambio: null,
      subtotal: 0,
      impuestosTrasladados: 0,
      retenciones: 0,
      total: 0,
      tipo: TipoNotaCredito.Descuento,
      tipoRelacionCfdi: TipoRelacionCfdi.NotaCredito,
      uuidRelacionCfdi: '',
    },
  });

  /** Prellena relación (01/03/07) y UUID origen desde el XML parseado. */
  function aplicarRelacionParseada(parseado: CfdiParseado) {
    const rel = parseado.cfdiRelacionados?.[0];
    if (!rel || rel.uuids.length === 0) return;
    const tipoRel = TIPO_RELACION_POR_CODIGO[rel.tipoRelacion];
    if (tipoRel !== undefined) {
      form.setValue('tipoRelacionCfdi', tipoRel);
      form.setValue('tipo', TIPO_NC_POR_RELACION[tipoRel]);
    }
    form.setValue('uuidRelacionCfdi', rel.uuids[0], { shouldValidate: true });
  }

  /**
   * Auto-resuelve el proveedor desde el RFC emisor del CFDI (match exacto
   * server-side). Solo llena si el campo está vacío — nunca pisa una
   * selección previa. Con 0 o >1 coincidencias, el selector queda manual.
   */
  async function resolverProveedorPorRfc(rfcEmisor: string) {
    if (form.getValues('proveedorId')) return;
    try {
      const matches = await fetchProveedoresPorRfc(rfcEmisor);
      if (matches.length === 1) {
        form.setValue('proveedorId', matches[0].id, { shouldValidate: true });
      } else if (matches.length === 0) {
        toast.info(
          `Ningún proveedor activo con RFC ${rfcEmisor}; selecciónalo manualmente.`,
        );
      }
    } catch {
      // Selector manual como respaldo.
    }
  }

  /**
   * Vincula el CFDI de egreso y prellena encabezado + importes desde los
   * metadatos persistidos; la relación viene del re-parseo on-demand del
   * XML y el proveedor del RFC emisor (ambos best-effort: si fallan, se
   * capturan a mano).
   */
  async function vincularCfdi(cfdi: CfdiListItem | null) {
    setCfdiSel(cfdi);
    form.setValue('cfdiRecibidoId', cfdi?.id ?? null);
    form.setValue('uuidCfdi', cfdi?.uuidCfdi ?? '', { shouldValidate: true });
    if (!cfdi) return;
    form.setValue('serieProveedor', cfdi.serie);
    form.setValue('folioProveedor', cfdi.folio);
    form.setValue('fechaCfdi', cfdi.fechaCfdi.slice(0, 10));
    form.setValue('moneda', cfdi.moneda);
    form.setValue('tipoCambio', cfdi.tipoCambio ?? null);
    form.setValue('subtotal', cfdi.subtotal);
    form.setValue('impuestosTrasladados', cfdi.impuestosTrasladados);
    form.setValue('retenciones', cfdi.retenciones);
    form.setValue('total', cfdi.total, { shouldValidate: true });
    await Promise.all([
      resolverProveedorPorRfc(cfdi.rfcEmisor),
      fetchCfdiParseado(cfdi.id)
        .then(aplicarRelacionParseada)
        .catch(() => {
          // Sin relación prellenada; el usuario la teclea como respaldo.
        }),
    ]);
  }

  function onSubmit(values: CapturarNotaCreditoValues) {
    capturar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('NC capturada');
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            )
              return;
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al capturar NC.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="space-y-1">
        <Label className="text-xs">CFDI de la NC (egreso)</Label>
        <div className="flex gap-2">
          <CfdiPorProcesarPicker
            tipo={TipoCfdi.Egreso}
            value={cfdiSel?.id ?? null}
            onSelect={vincularCfdi}
            placeholder="Vincular CFDI de egreso…"
            className="flex-1"
          />
          <Button
            type="button"
            variant="outline"
            onClick={() => setCargarCfdiOpen(true)}
          >
            <Upload className="mr-1 h-4 w-4" aria-hidden="true" />
            Cargar XML
          </Button>
        </div>
        <p className="text-xs text-muted-foreground">
          Al vincular se prellenan UUID, importes y la relación con la
          factura origen desde el XML.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo label="UUID CFDI" error={form.formState.errors.uuidCfdi?.message} required>
          <Input
            placeholder="00000000-…"
            readOnly={cfdiSel !== null}
            className={cfdiSel ? 'bg-muted font-mono text-xs' : undefined}
            {...form.register('uuidCfdi')}
          />
        </Campo>
        <Campo label="Proveedor" error={form.formState.errors.proveedorId?.message} required>
          <Controller
            control={form.control}
            name="proveedorId"
            render={({ field }) => (
              <ProveedorSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo label="Serie" error={form.formState.errors.serieProveedor?.message}>
          <Input placeholder="A" {...form.register('serieProveedor')} />
        </Campo>
        <Campo label="Folio" error={form.formState.errors.folioProveedor?.message}>
          <Input placeholder="12345" {...form.register('folioProveedor')} />
        </Campo>
        <Campo label="Fecha CFDI" error={form.formState.errors.fechaCfdi?.message} required>
          <Input type="date" {...form.register('fechaCfdi')} />
        </Campo>
        <Campo label="Moneda" required>
          <Input placeholder="MXN" maxLength={3} className="uppercase" {...form.register('moneda')} />
        </Campo>
        <Campo label="Tipo NC" required>
          <Controller
            control={form.control}
            name="tipo"
            render={({ field }) => (
              <Select value={String(field.value)} onValueChange={(v) => field.onChange(Number(v))}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {[
                    TipoNotaCredito.Descuento,
                    TipoNotaCredito.Devolucion,
                    TipoNotaCredito.AmortizacionAnticipo,
                  ].map((t) => (
                    <SelectItem key={t} value={String(t)}>
                      {TipoNotaCreditoLabels[t]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </Campo>
        <Campo label="UUID CFDI relacionado" error={form.formState.errors.uuidRelacionCfdi?.message} required>
          <Input placeholder="00000000-…" {...form.register('uuidRelacionCfdi')} />
        </Campo>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Campo label="Subtotal" required>
          <Input type="number" step="0.01" min="0" {...form.register('subtotal', { valueAsNumber: true })} />
        </Campo>
        <Campo label="IVA trasladado">
          <Input type="number" step="0.01" min="0" {...form.register('impuestosTrasladados', { valueAsNumber: true })} />
        </Campo>
        <Campo label="Retenciones">
          <Input type="number" step="0.01" min="0" {...form.register('retenciones', { valueAsNumber: true })} />
        </Campo>
        <Campo label="Total" error={form.formState.errors.total?.message} required>
          <Input type="number" step="0.01" min="0" {...form.register('total', { valueAsNumber: true })} />
        </Campo>
      </div>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={capturar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={capturar.isPending}>
          {capturar.isPending ? 'Capturando…' : 'Capturar NC'}
        </Button>
      </SheetFooter>

      {/* Sheet apilado: carga del XML de la NC (canal CargaManual). Al
          cargar, se vincula y prellena igual que desde el picker. */}
      <CargarCfdiSheet
        open={cargarCfdiOpen}
        onOpenChange={setCargarCfdiOpen}
        onCargado={(resp) => {
          fetchCfdiDetalle(resp.id)
            .then((det) => vincularCfdi(det))
            .catch(() => {
              // Fallback mínimo: vínculo + UUID; el resto a mano.
              form.setValue('cfdiRecibidoId', resp.id);
              form.setValue('uuidCfdi', resp.uuidCfdi, {
                shouldValidate: true,
              });
            });
        }}
      />
    </form>
  );
}

interface CampoProps {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}

function Campo({ label, required, error, children }: CampoProps) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
