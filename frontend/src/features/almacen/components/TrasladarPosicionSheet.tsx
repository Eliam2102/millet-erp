import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { TextAreaField } from '@/components/erp';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { Field } from '@/features/almacen/components/internal/Field';
import { useTrasladarPosicion } from '@/features/almacen/api/useSaldosCierreReportes';
import type { SaldoUbicacionItem } from '@/features/almacen/api/types';
import { hojasDe, textoMedida } from '@/features/almacen/lib/medida-hoja';
import { codigoSoporte, esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';

interface Props {
  articuloId: string;
  /** Fila origen (hoja/medida/lote en su posición); null cierra la hoja. */
  fila: SaldoUbicacionItem | null;
  onOpenChange: (open: boolean) => void;
  /** Vuelve a leer las posiciones cuando otro usuario movió la existencia. */
  onRecargar: () => void;
}

/** M-231: traslado de hojas (o existencia sin medida) entre posiciones del mismo almacén. */
export function TrasladarPosicionSheet({ articuloId, fila, onOpenChange, onRecargar }: Props) {
  return (
    <Sheet open={fila !== null} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Trasladar entre posiciones</SheetTitle>
          <SheetDescription>Mueve la existencia a otra posición del mismo almacén, con su lote y costo. No genera póliza.</SheetDescription>
        </SheetHeader>
        {fila && <Formulario key={fila.saldoId} articuloId={articuloId} fila={fila} onCerrar={() => onOpenChange(false)} onRecargar={onRecargar} />}
      </SheetContent>
    </Sheet>
  );
}

function Formulario({ articuloId, fila, onCerrar, onRecargar }: { articuloId: string; fila: SaldoUbicacionItem; onCerrar: () => void; onRecargar: () => void }) {
  const keyFor = useBodyScopedIdempotencyKey();
  const trasladar = useTrasladarPosicion();
  const [cantidad, setCantidad] = useState('');
  const [destinoId, setDestinoId] = useState<string | null>(null);
  const [motivo, setMotivo] = useState<string | null>(null);
  const [errores, setErrores] = useState<Record<string, string>>({});
  const porMedida = !!fila.productoAwVarianteId;
  const disponible = porMedida ? hojasDe(fila.cantidadDisponible, fila.areaHojaM2) : fila.cantidadDisponible;
  const unidad = porMedida ? 'hojas' : 'unidades';

  function enviar(e: React.FormEvent) {
    e.preventDefault();
    const n = Number(cantidad);
    const faltan: Record<string, string> = {};
    if (!(n > 0)) faltan.cantidad = 'La cantidad debe ser mayor que cero.';
    else if (porMedida && !Number.isInteger(n)) faltan.cantidad = 'Se trasladan hojas enteras.';
    else if (disponible != null && n > disponible) faltan.cantidad = `Solo hay ${disponible} ${unidad} disponibles.`;
    if (!destinoId) faltan.destino = 'Elige la posición destino.';
    else if (destinoId === fila.ubicacionId) faltan.destino = 'Elige una posición distinta de la de origen.';
    setErrores(faltan);
    if (Object.keys(faltan).length > 0 || !fila.saldoId || !fila.version) return;
    const command = { saldoId: fila.saldoId, version: fila.version, cantidad: n, ubicacionDestinoId: destinoId!, motivo: motivo?.trim() || null };
    trasladar.mutate(
      { command, idempotencyKey: keyFor(command) },
      {
        onSuccess: (r) => {
          toast.success(`Traslado registrado: ${r.folio}`);
          onCerrar();
        },
        onError: (error) => {
          if (esApiError(error) && error.code === 'TRASLADO_SALDO_CAMBIO') {
            toast.error('La existencia cambió, recarga.', { description: 'Otro usuario movió esta posición; ya se actualizó la lista.' });
            onRecargar();
            return;
          }
          toast.error(errorUsuario(error, 'No se pudo trasladar'), { description: esApiError(error) ? codigoSoporte(error) : undefined });
        },
      },
    );
  }

  return (
    <>
      <SheetBody>
        <form id="traslado-form" onSubmit={enviar} className="space-y-4" noValidate>
          <dl className="grid grid-cols-2 gap-x-4 gap-y-1 rounded-md border bg-surface-muted/30 p-3 text-sm">
            <dt className="text-ink-muted">Origen</dt>
            <dd className="font-mono">{fila.almacenClave} › {fila.subAlmacenClave} · {fila.clave}</dd>
            <dt className="text-ink-muted">Medida</dt>
            <dd>{textoMedida(fila, fila.cantidadDisponible) ?? 'Sin medida'}</dd>
            <dt className="text-ink-muted">Lote</dt>
            <dd className="font-mono">{fila.lote ?? '—'}</dd>
            <dt className="text-ink-muted">Disponible</dt>
            <dd>{disponible ?? fila.cantidadDisponible} {unidad}</dd>
          </dl>
          <Field label={porMedida ? 'Hojas a trasladar' : 'Cantidad a trasladar'} required error={errores.cantidad}>
            <Input type="number" min="0" step={porMedida ? 1 : 'any'} value={cantidad} onChange={(e) => setCantidad(e.target.value)} />
          </Field>
          <Field label="Posición destino" required error={errores.destino}>
            <UbicacionBinSelector
              modo="asignacion"
              articuloId={articuloId}
              subAlmacenId={null}
              requiereSubAlmacen={false}
              almacenClave={fila.almacenClave}
              value={destinoId}
              onChange={setDestinoId}
            />
          </Field>
          <Field label="Motivo">
            <TextAreaField value={motivo} onChange={setMotivo} maxLength={300} minRows={2} />
          </Field>
        </form>
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCerrar} disabled={trasladar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="traslado-form" disabled={trasladar.isPending}>
          {trasladar.isPending ? 'Trasladando…' : 'Trasladar'}
        </Button>
      </SheetFooter>
    </>
  );
}
