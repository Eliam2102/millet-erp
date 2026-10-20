import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { DialogBody } from '@/components/ui/dialog';
import { useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { CondicionesPagoSelector, Field, UsoPrincipalSelector } from '@/components/erp';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import type { ComparativoCotizaciones, CotizacionComparativo } from '@/features/compras/api/types';
import { useCrearOrdenCompraDesdeRequisicion } from '@/features/compras/ordenes/api/useCrearOrdenCompraDesdeRequisicion';
import { useSucursalesPermitidas } from '@/features/compras/ordenes/api/useSucursalesPermitidas';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';

export interface GenerarOcCotizacionDialogProps {
  abierto: boolean;
  onClose: () => void;
  data: ComparativoCotizaciones;
  cotizacion: CotizacionComparativo;
  motivoEleccion: string;
}

function Formulario({
  onClose,
  data,
  cotizacion,
  motivoEleccion,
}: Omit<GenerarOcCotizacionDialogProps, 'abierto'>) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearOrdenCompraDesdeRequisicion();
  const { data: permitidas } = useSucursalesPermitidas();
  const esMxn = cotizacion.moneda === 'MXN';
  const tcDelDia = data.tiposCambio.find((t) => t.moneda === cotizacion.moneda)?.valorEnMxn ?? null;

  const [condicionesPagoId, setCondicionesPagoId] = useState('');
  const [usoPrincipalId, setUsoPrincipalId] = useState('');
  const [fechaEntrega, setFechaEntrega] = useState('');
  const [tipoCambio, setTipoCambio] = useState(tcDelDia === null ? '' : String(tcDelDia));
  const [observaciones, setObservaciones] = useState('');

  const tc = Number(tipoCambio);
  const faltante = !condicionesPagoId
    ? 'condiciones de pago'
    : !usoPrincipalId
      ? 'uso principal'
      : !esMxn && !(tc > 0)
        ? 'tipo de cambio'
        : null;

  async function enviar() {
    const sucursalCodigo = permitidas?.find((s) => s.id === data.sucursalId)?.clave;
    if (!sucursalCodigo) {
      toast.error('No se pudo resolver el código de sucursal.');
      return;
    }
    try {
      const r = await crear.mutateAsync({
        command: {
          requisicionId: data.requisicionId,
          sucursalCodigo,
          folioAnio: new Date().getFullYear(),
          proveedorId: cotizacion.proveedorId,
          condicionesPagoId,
          usoPrincipalId,
          fechaDocumento: hoyLocalISO(),
          moneda: cotizacion.moneda,
          tipoCambio: esMxn ? null : tc,
          fechaEntregaEsperada: fechaEntrega || null,
          observaciones: observaciones.trim() || null,
          cotizacionId: cotizacion.id,
          motivoEleccion: motivoEleccion.trim(),
        },
        idempotencyKey,
      });
      toast.success(`OC creada: ${r.folio}`);
      onClose();
      await navigate({ to: '/compras/ordenes/$id', params: { id: r.ordenCompraId } });
    } catch (err) {
      if (esApiError(err)) {
        toast.error(err.problem.title, { description: err.problem.detail });
      } else {
        toast.error('Error inesperado al generar la orden de compra.');
      }
    }
  }

  return (
    <>
      <DialogBody>
        <FormSeccion titulo="Datos de la orden">
          <FormCampo span={6}>
            <Field label="Condiciones de pago" required>
              {(c) => (
                <CondicionesPagoSelector
                  triggerProps={c}
                  value={condicionesPagoId || null}
                  onChange={(v) => setCondicionesPagoId(v ?? '')}
                />
              )}
            </Field>
          </FormCampo>
          <FormCampo span={6}>
            <Field label="Uso principal" required>
              {(c) => (
                <UsoPrincipalSelector
                  triggerProps={c}
                  value={usoPrincipalId || null}
                  onChange={(v) => setUsoPrincipalId(v ?? '')}
                />
              )}
            </Field>
          </FormCampo>
          <FormCampo span={6}>
            {!esMxn && (
              <Field label={`Tipo de cambio (${cotizacion.moneda} → MXN)`} required>
                <Input
                  type="number"
                  step="any"
                  min={0}
                  inputMode="decimal"
                  className="tabular-nums"
                  value={tipoCambio}
                  onChange={(e) => setTipoCambio(e.target.value)}
                />
              </Field>
            )}
          </FormCampo>
          <FormCampo span={6}>
            <Field label="Fecha de entrega esperada" optional>
              <Input
                type="date"
                value={fechaEntrega}
                onChange={(e) => setFechaEntrega(e.target.value)}
              />
            </Field>
          </FormCampo>
          <FormCampo span={6}>
            <Field label="Observaciones" optional>
              <Textarea
                rows={3}
                maxLength={1000}
                value={observaciones}
                onChange={(e) => setObservaciones(e.target.value)}
              />
            </Field>
          </FormCampo>
        </FormSeccion>
      </DialogBody>
      <DialogFooter className="items-center gap-2 sm:justify-between">
        <p role="status" className="text-xs text-ink-muted">
          {faltante ? `Falta: ${faltante}` : ''}
        </p>
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={onClose} disabled={crear.isPending}>
            Cancelar
          </Button>
          <Button
            type="button"
            disabled={crear.isPending || faltante !== null}
            onClick={() => void enviar()}
          >
            {crear.isPending ? 'Generando…' : 'Generar orden de compra'}
          </Button>
        </div>
      </DialogFooter>
    </>
  );
}

/** F1-COM-02 — completa los datos de la OC que se genera con la cotización elegida (D1). */
export function GenerarOcCotizacionDialog({
  abierto,
  onClose,
  ...resto
}: GenerarOcCotizacionDialogProps) {
  const proveedor = resto.cotizacion.proveedorRazonSocial ?? resto.cotizacion.proveedorId;
  return (
    <Dialog open={abierto} onOpenChange={(o) => !o && onClose()}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>Generar orden de compra</DialogTitle>
          <DialogDescription>
            {proveedor} · {resto.cotizacion.moneda}
          </DialogDescription>
        </DialogHeader>
        {abierto && <Formulario onClose={onClose} {...resto} />}
      </DialogContent>
    </Dialog>
  );
}
