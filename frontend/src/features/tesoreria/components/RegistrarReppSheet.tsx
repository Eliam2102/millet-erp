import { apiFetch } from '@/lib/auth/api-client';
import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { toast } from 'sonner';
import { Upload } from 'lucide-react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { useRegistrarReppRecibido } from '@/features/tesoreria/api/useTesoreria';
import type { ReppPendienteResponse } from '@/features/tesoreria/api/types';
import { esUuidValido } from '@/features/tesoreria/lib/uuid';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { fetchCfdiDetalle } from '@/features/cxp/api/useCfdis';
import { TipoCfdi, type CfdiListItem } from '@/features/cxp/api/types';

export interface RegistrarReppSheetProps {
  pendiente: ReppPendienteResponse | null;
  pendientes?: ReppPendienteResponse[];
  onOpenChange: (open: boolean) => void;
}

/**
 * Sheet "Registrar REPP recibido" (§3.6.b / TES-4): vincula el complemento
 * de pago (CFDI tipo P) desde el repositorio de CFDIs de CxP — picker de
 * PorProcesar + "Cargar XML" (canal CargaManual) — y prellena UUID y
 * fecha, mismo molde que la captura de NC de CxP. El XML vive en el
 * repositorio documental de CxP; requiere el XML y el desglose de pagos. Registrar
 * publica <c>repp-proveedor.recibido.v1</c> → CxP libera FALTA_REPP. La
 * validación fiscal del UUID contra el SAT llega post-MVP [T-G10].
 *
 * <para>Patrón "remount on open": el form solo monta con pendiente, así
 * cada apertura arranca limpio sin resets manuales.</para>
 */
export function RegistrarReppSheet({
  pendiente,
  pendientes = [],
  onOpenChange,
}: RegistrarReppSheetProps) {
  return (
    <Sheet open={pendiente != null} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Registrar REPP recibido</SheetTitle>
          <SheetDescription>
            {pendiente != null &&
              `${pendiente.proveedorRazonSocial ?? pendiente.proveedorClave ?? 'Proveedor'} · factura ${
                pendiente.folioProveedor ?? pendiente.facturaProveedorId.slice(0, 8)
              } · pagado ${formatoMonto(pendiente.montoPagado, pendiente.moneda)} el ${formatoFecha(pendiente.fechaPrimerPago)}.`}
          </SheetDescription>
        </SheetHeader>
        {pendiente != null && (
          <ReppForm pendiente={pendiente} pendientes={pendientes.filter(p => p.facturaProveedorId === pendiente.facturaProveedorId)} onClose={() => onOpenChange(false)} />
        )}
      </SheetContent>
    </Sheet>
  );
}

function ReppForm({
  pendiente,
  pendientes,
  onClose,
}: {
  pendiente: ReppPendienteResponse;
  pendientes: ReppPendienteResponse[];
  onClose: () => void;
}) {
  const registrar = useRegistrarReppRecibido();
  const pagosDisponibles = pendientes.length > 0 ? pendientes : [pendiente];
  const [importes, setImportes] = useState<Record<string, string>>({ [pendiente.pagoId]: String(pendiente.importePendiente) });
  const pagos = pagosDisponibles.filter(p => importes[p.pagoId] !== undefined).map(p => ({ pagoId: p.pagoId, importe: Number(importes[p.pagoId]) }));
  const idempotencyKey = useFormIdempotencyKey();
  const [xmlBase64, setXmlBase64] = useState('');
  const [cargandoXml, setCargandoXml] = useState(false);
  const hoy = hoyLocalISO();

  const [cfdiSel, setCfdiSel] = useState<CfdiListItem | null>(null);
  const [cargarCfdiOpen, setCargarCfdiOpen] = useState(false);
  const puedeCargarCfdi = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisCargarManual,
  );
  const [uuid, setUuid] = useState('');
  const [fecha, setFecha] = useState(hoy);

  const uuidValido = esUuidValido(uuid);
  const valido = uuidValido && fecha !== '' && xmlBase64 !== '' && !cargandoXml && pagos.length > 0 && pagos.every(p => Number.isFinite(p.importe) && p.importe > 0 && p.importe <= pagosDisponibles.find(d => d.pagoId === p.pagoId)!.importePendiente);

  /** Prellena UUID y fecha desde el CFDI vinculado; al quitar, limpia. */
  async function vincular(cfdi: CfdiListItem | null) {
    setXmlBase64('');
    if (cfdi != null && cfdi.tipo !== TipoCfdi.Pago) {
      toast.error('El CFDI no es un complemento de pago (tipo P).', {
        description: `El UUID ${cfdi.uuidCfdi} quedó en el repositorio de CxP sin vincular.`,
      });
      return;
    }
    setCfdiSel(cfdi);
    if (cfdi != null) {
      setUuid(cfdi.uuidCfdi);
      setFecha(cfdi.fechaCfdi.slice(0, 10));
      setCargandoXml(true);
      try {
        const response = await apiFetch(`/api/v1/cuentas-por-pagar/cfdis/${cfdi.id}/xml`);
        if (!response.ok) throw new Error('No se pudo leer el XML.');
        const bytes = new Uint8Array(await response.arrayBuffer());
        setXmlBase64(btoa(Array.from(bytes, b => String.fromCharCode(b)).join('')));
      } catch { toast.error('No se pudo leer el XML del complemento. Vuelve a seleccionarlo.'); }
      finally { setCargandoXml(false); }
    } else {
      setUuid('');
      setFecha(hoy);
    }
  }

  function confirmar() {
    if (!valido) return;
    registrar.mutate(
      {
        command: {
          facturaProveedorId: pendiente.facturaProveedorId,
          uuidComplemento: uuid.trim(),
          fechaComplemento: fecha,
          xmlBase64,
          pagos,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('REPP registrado', {
            description:
              'El complemento validado cubre este pago. CxP liberará FALTA_REPP cuando estén cubiertos los pagos pendientes.',
          });
          onClose();
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo registrar el REPP',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? `Código: ${error.traceId}`)
                : undefined,
            },
          );
        },
      },
    );
  }

  return (
    <div className="space-y-4 px-4 py-4">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">
          CFDI del complemento (tipo P)
        </label>
        <div className="flex gap-2">
          <CfdiPorProcesarPicker
            tipo={TipoCfdi.Pago}
            value={cfdiSel?.id ?? null}
            onSelect={vincular}
            placeholder="Vincular complemento de pago…"
            className="flex-1"
          />
          {puedeCargarCfdi && (
            <Button
              type="button"
              variant="outline"
              onClick={() => setCargarCfdiOpen(true)}
            >
              <Upload className="mr-1 h-4 w-4" aria-hidden="true" />
              Cargar XML
            </Button>
          )}
        </div>
        <p className="text-xs text-muted-foreground">
          Al vincular se prellenan el UUID y la fecha desde el XML. Los
          complementos que llegan por descarga SAT o mailbox ya aparecen en
          la lista; si no está, cárgalo con el XML del proveedor.
        </p>
      </div>

      <div className="space-y-1">
        <label className="text-xs text-muted-foreground" htmlFor="repp-uuid">
          UUID del complemento (folio fiscal)
        </label>
        <Input
          id="repp-uuid"
          value={uuid}
          onChange={(e) => setUuid(e.target.value)}
          placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
          readOnly={cfdiSel != null}
          className={cfdiSel != null ? 'bg-muted font-mono' : 'font-mono'}
          maxLength={36}
        />
        {uuid.length > 0 && !uuidValido && (
          <p className="text-xs text-danger-fg">
            Debe ser el UUID del timbre del CFDI tipo P (8-4-4-4-12).
          </p>
        )}
      </div>

      <div className="space-y-1">
        <label className="text-xs text-muted-foreground" htmlFor="repp-fecha">
          Fecha del complemento
        </label>
        <Input
          id="repp-fecha"
          type="date"
          value={fecha}
          onChange={(e) => setFecha(e.target.value)}
        />
      </div>

      <fieldset className="space-y-3"><legend className="mb-2 text-sm font-medium text-ink">Pagos que cubre el XML</legend>
        {pagosDisponibles.map(p => <div key={p.pagoId} className="space-y-1">
          <label className="flex items-center gap-2 text-sm"><Checkbox checked={importes[p.pagoId] !== undefined} onCheckedChange={checked => setImportes(prev => { const next = { ...prev }; if (checked === true) next[p.pagoId] = String(p.importePendiente); else delete next[p.pagoId]; return next; })} />{formatoFecha(p.fechaPrimerPago)} · pendiente {formatoMonto(p.importePendiente, p.moneda)}</label>
          {importes[p.pagoId] !== undefined && <Input aria-label={`Importe del pago ${p.pagoId}`} type="number" min="0.01" step="0.01" max={p.importePendiente} value={importes[p.pagoId]} onChange={e => setImportes(prev => ({ ...prev, [p.pagoId]: e.target.value }))} />}
        </div>)}
      </fieldset>
      <div className="flex justify-end gap-2 pt-2">
        <Button
          variant="ghost"
          onClick={onClose}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button onClick={confirmar} disabled={registrar.isPending || !valido} title={!valido ? 'Selecciona el XML del complemento para validar el pago.' : undefined}>
          {registrar.isPending ? 'Registrando…' : 'Registrar REPP'}
        </Button>
      </div>

      {/* Sheet apilado: carga del XML del complemento (canal CargaManual,
          repositorio documental de CxP). Al cargar, se vincula y prellena
          igual que desde el picker. */}
      <CargarCfdiSheet
        open={cargarCfdiOpen}
        onOpenChange={setCargarCfdiOpen}
        onCargado={(resp) => {
          fetchCfdiDetalle(resp.id)
            .then((det) => vincular(det))
            .catch(() => {
              // Fallback mínimo: UUID prellenado; la fecha se captura a mano.
              setUuid(resp.uuidCfdi);
            });
        }}
      />
    </div>
  );
}
