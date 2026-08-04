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
import { Input } from '@/components/ui/input';
import { useRegistrarReppRecibido } from '@/features/tesoreria/api/useTesoreria';
import type { ReppPendienteResponse } from '@/features/tesoreria/api/types';
import { esUuidValido } from '@/features/tesoreria/lib/uuid';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { esApiError } from '@/lib/api';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { fetchCfdiDetalle } from '@/features/cxp/api/useCfdis';
import { TipoCfdi, type CfdiListItem } from '@/features/cxp/api/types';

export interface RegistrarReppSheetProps {
  pendiente: ReppPendienteResponse | null;
  onOpenChange: (open: boolean) => void;
}

/**
 * Sheet "Registrar REPP recibido" (§3.6.b / TES-4): vincula el complemento
 * de pago (CFDI tipo P) desde el repositorio de CFDIs de CxP — picker de
 * PorProcesar + "Cargar XML" (canal CargaManual) — y prellena UUID y
 * fecha, mismo molde que la captura de NC de CxP. El XML vive en el
 * repositorio documental de CxP; el registro manual con UUID a mano sigue
 * disponible como respaldo (T-G3, p. ej. solo llegó el PDF). Registrar
 * publica <c>repp-proveedor.recibido.v1</c> → CxP libera FALTA_REPP. La
 * validación fiscal del UUID contra el SAT llega post-MVP [T-G10].
 *
 * <para>Patrón "remount on open": el form solo monta con pendiente, así
 * cada apertura arranca limpio sin resets manuales.</para>
 */
export function RegistrarReppSheet({
  pendiente,
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
          <ReppForm pendiente={pendiente} onClose={() => onOpenChange(false)} />
        )}
      </SheetContent>
    </Sheet>
  );
}

function ReppForm({
  pendiente,
  onClose,
}: {
  pendiente: ReppPendienteResponse;
  onClose: () => void;
}) {
  const registrar = useRegistrarReppRecibido();
  const hoy = hoyLocalISO();

  const [cfdiSel, setCfdiSel] = useState<CfdiListItem | null>(null);
  const [cargarCfdiOpen, setCargarCfdiOpen] = useState(false);
  const [uuid, setUuid] = useState('');
  const [fecha, setFecha] = useState(hoy);

  const uuidValido = esUuidValido(uuid);
  const valido = uuidValido && fecha !== '';

  /** Prellena UUID y fecha desde el CFDI vinculado; al quitar, limpia. */
  function vincular(cfdi: CfdiListItem | null) {
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
        },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('REPP registrado', {
            description:
              'CxP recibirá repp-proveedor.recibido.v1 y liberará el motivo FALTA_REPP.',
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
          <p className="text-xs text-rose-600">
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

      <div className="flex justify-end gap-2 pt-2">
        <Button
          variant="ghost"
          onClick={onClose}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button onClick={confirmar} disabled={registrar.isPending || !valido}>
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
