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
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CapturarAnticipoSchema,
  type CapturarAnticipoValues,
} from '@/features/cxp/schemas/notas-y-anticipos';
import { useCapturarAnticipo } from '@/features/cxp/api/useNotasYAnticipos';
import { fetchCfdiDetalle } from '@/features/cxp/api/useCfdis';
import type { CfdiListItem } from '@/features/cxp/api/types';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { fetchProveedoresPorRfc } from '@/features/catalogos/api';
import { OrdenCompraSelector, ProveedorSelector } from '@/components/erp';

/**
 * <c>&lt;NuevoAnticipoSheet/&gt;</c> — captura de anticipo a proveedor
 * (CFDI serie FANT). El backend publica el evento de integración para
 * que Compras lo asocie a la OC referenciada.
 */
export interface NuevoAnticipoSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevoAnticipoSheet({
  open,
  onOpenChange,
}: NuevoAnticipoSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Capturar anticipo a proveedor</SheetTitle>
          <SheetDescription>
            CFDI con serie FANT. Se publica evento para que Compras lo
            asocie a la OC.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const capturar = useCapturarAnticipo();
  const hoy = hoyLocalISO();

  const [cfdiSel, setCfdiSel] = useState<CfdiListItem | null>(null);
  const [cargarCfdiOpen, setCargarCfdiOpen] = useState(false);

  const form = useForm<CapturarAnticipoValues>({
    resolver: zodResolver(CapturarAnticipoSchema),
    defaultValues: {
      cfdiRecibidoId: null,
      uuidCfdi: '',
      proveedorId: '',
      serie: 'FANT',
      folioProveedor: null,
      fechaCfdi: hoy,
      moneda: 'MXN',
      tipoCambio: null,
      montoEntregado: 0,
      ordenCompraId: null,
    },
  });

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
   * Vincula el CFDI del anticipo (tipo Ingreso, típicamente serie FANT)
   * y prellena encabezado + monto desde los metadatos persistidos; el
   * proveedor se auto-resuelve por RFC emisor (best-effort).
   */
  async function vincularCfdi(cfdi: CfdiListItem | null) {
    setCfdiSel(cfdi);
    form.setValue('cfdiRecibidoId', cfdi?.id ?? null);
    form.setValue('uuidCfdi', cfdi?.uuidCfdi ?? '', { shouldValidate: true });
    if (!cfdi) return;
    if (cfdi.serie) form.setValue('serie', cfdi.serie);
    form.setValue('folioProveedor', cfdi.folio);
    form.setValue('fechaCfdi', cfdi.fechaCfdi.slice(0, 10));
    form.setValue('moneda', cfdi.moneda);
    form.setValue('tipoCambio', cfdi.tipoCambio ?? null);
    form.setValue('montoEntregado', cfdi.total, { shouldValidate: true });
    await resolverProveedorPorRfc(cfdi.rfcEmisor);
  }

  function onSubmit(values: CapturarAnticipoValues) {
    capturar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Anticipo capturado');
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
          toast.error('Error al capturar anticipo.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="space-y-1">
        <Label className="text-xs">CFDI del anticipo</Label>
        <div className="flex gap-2">
          <CfdiPorProcesarPicker
            value={cfdiSel?.id ?? null}
            onSelect={vincularCfdi}
            placeholder="Vincular CFDI del anticipo…"
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
          Al vincular se prellenan UUID, serie, fecha y monto desde el XML.
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
        <Campo label="Serie" error={form.formState.errors.serie?.message} required>
          <Input placeholder="FANT" {...form.register('serie')} />
        </Campo>
        <Campo label="Folio">
          <Input placeholder="12345" {...form.register('folioProveedor')} />
        </Campo>
        <Campo label="Fecha CFDI" required>
          <Input type="date" {...form.register('fechaCfdi')} />
        </Campo>
        <Campo label="Moneda" required>
          <Input placeholder="MXN" maxLength={3} className="uppercase" {...form.register('moneda')} />
        </Campo>
        <Campo label="Monto entregado" error={form.formState.errors.montoEntregado?.message} required>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoEntregado', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Orden de compra (opcional)">
          <Controller
            control={form.control}
            name="ordenCompraId"
            render={({ field }) => (
              <OrdenCompraSelector
                value={field.value ?? null}
                onChange={(id) => field.onChange(id)}
              />
            )}
          />
        </Campo>
      </div>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={capturar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={capturar.isPending}>
          {capturar.isPending ? 'Capturando…' : 'Capturar anticipo'}
        </Button>
      </SheetFooter>

      {/* Sheet apilado: carga del XML del anticipo (canal CargaManual).
          Al cargar, se vincula y prellena igual que desde el picker. */}
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

function Campo({
  label,
  required,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
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
