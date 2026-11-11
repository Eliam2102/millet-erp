import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useAutorizarReversaMovimiento,
  useCorreccionesCartera,
  useCorregirPlazo,
  useRechazarReversaMovimiento,
  useSolicitarReversaMovimiento,
} from '@/features/cxc/api/useCorreccionesCartera';
import { CorreccionCarteraDialog, type ModoCorreccion } from '@/features/cxc/components/CorreccionCarteraDialog';

type Fila = Record<string, unknown>;

interface Objetivo {
  modo: ModoCorreccion;
  /** Id de factura (plazo), de movimiento (reversa) o de la solicitud (rechazar). */
  id: string;
  texto: string;
  plazoActualDias?: number;
}

const importe = (v: unknown) =>
  typeof v === 'number' ? v.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '';

function mensajeError(err: unknown) {
  return esApiError(err) ? (err.problem.detail ?? err.problem.title) : err instanceof Error ? err.message : String(err);
}

/**
 * M-237 (CXC-CARTERA/corregir): acciones de corrección sobre el estado de cuenta — corregir el plazo de una factura, solicitar la
 * reversa de un movimiento mal aplicado y, con otro permiso, autorizar o rechazar las solicitudes pendientes. El rastro (quién, por qué,
 * antes/después) aparece como filas del propio estado de cuenta.
 */
export function PanelCorreccionesCartera({ clienteId, filas }: { clienteId: string; filas: Fila[] }) {
  const puedeCorregir = useHasPermission(PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar);
  const pendientes = useCorreccionesCartera(clienteId, 'Pendiente');
  const corregirPlazo = useCorregirPlazo(clienteId);
  const solicitar = useSolicitarReversaMovimiento(clienteId);
  const autorizar = useAutorizarReversaMovimiento(clienteId);
  const rechazar = useRechazarReversaMovimiento(clienteId);
  const [objetivo, setObjetivo] = useState<Objetivo | null>(null);

  const facturas = filas.filter((f) => f.tipo === 'Factura' && typeof f.factura_cartera_id === 'string');
  const movimientos = filas.filter(
    (f) => (f.tipo === 'Pago' || f.tipo === 'Nota de crédito') && typeof f.movimiento_id === 'string',
  );
  const solicitudes = (pendientes.data ?? []).filter((c) => c.tipo === 'ReversaMovimiento');
  if (!puedeCorregir && !puedeAutorizar) return null;
  if (facturas.length === 0 && solicitudes.length === 0) return null;

  async function confirmar(datos: { motivo: string; plazoDias?: number }) {
    if (!objetivo) return;
    try {
      if (objetivo.modo === 'plazo') {
        await corregirPlazo.mutateAsync({ facturaCarteraId: objetivo.id, plazoDias: datos.plazoDias!, motivo: datos.motivo });
        toast.success('Plazo corregido', { description: 'El vencimiento y la antigüedad se recalcularon.' });
      } else if (objetivo.modo === 'reversa') {
        await solicitar.mutateAsync({ movimientoId: objetivo.id, motivo: datos.motivo });
        toast.success('Reversa solicitada', { description: 'Queda pendiente de autorización de otra persona.' });
      } else {
        await rechazar.mutateAsync({ id: objetivo.id, motivo: datos.motivo });
        toast.success('Reversa rechazada');
      }
      setObjetivo(null);
    } catch (err) {
      toast.error('No se pudo registrar la corrección', { description: mensajeError(err) });
    }
  }

  async function autorizarSolicitud(id: string) {
    try {
      await autorizar.mutateAsync({ id });
      toast.success('Reversa autorizada', { description: 'El saldo regresó a la factura.' });
    } catch (err) {
      toast.error('No se pudo autorizar la reversa', { description: mensajeError(err) });
    }
  }

  const enCurso = corregirPlazo.isPending || solicitar.isPending || rechazar.isPending;

  return (
    <section data-print="hidden" aria-labelledby="correcciones-cartera" className="space-y-3 rounded-md border bg-surface p-4">
      <h2 id="correcciones-cartera" className="text-sm font-semibold text-ink">
        Correcciones de cartera
      </h2>

      {solicitudes.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs text-ink-muted">Reversas pendientes de autorización</p>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Factura</TableHead>
                <TableHead>Movimiento</TableHead>
                <TableHead>Solicitó</TableHead>
                <TableHead>Motivo</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {solicitudes.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>{c.folio}</TableCell>
                  <TableCell>
                    {c.movimientoTipo} {importe(c.movimientoImporte)}
                  </TableCell>
                  <TableCell>
                    {c.solicitadaPorNombre ?? '—'} <Badge variant="neutral">{formatDate(c.solicitadaEn)}</Badge>
                  </TableCell>
                  <TableCell>{c.motivo}</TableCell>
                  <TableCell className="space-x-2 text-right">
                    {puedeAutorizar && (
                      <>
                        <Button size="sm" disabled={autorizar.isPending} onClick={() => void autorizarSolicitud(c.id)}>
                          Autorizar
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => setObjetivo({ modo: 'rechazar', id: c.id, texto: `Reversa de ${c.movimientoTipo} en ${c.folio}` })}
                        >
                          Rechazar
                        </Button>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {puedeCorregir && facturas.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs text-ink-muted">Facturas: corregir el plazo</p>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Factura</TableHead>
                <TableHead>Vence</TableHead>
                <TableHead>Plazo</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {facturas.map((f) => (
                <TableRow key={String(f.factura_cartera_id)}>
                  <TableCell>{String(f.referencia)}</TableCell>
                  <TableCell>{typeof f.vencimiento === 'string' ? formatDate(f.vencimiento) : '—'}</TableCell>
                  <TableCell>{String(f.plazo_dias)} días</TableCell>
                  <TableCell className="text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() =>
                        setObjetivo({
                          modo: 'plazo',
                          id: String(f.factura_cartera_id),
                          texto: `Factura ${String(f.referencia)}`,
                          plazoActualDias: Number(f.plazo_dias),
                        })
                      }
                    >
                      Corregir plazo
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {puedeCorregir && movimientos.length > 0 && (
        <div className="space-y-2">
          <p className="text-xs text-ink-muted">Movimientos mal aplicados: solicitar su reversa</p>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Fecha</TableHead>
                <TableHead>Movimiento</TableHead>
                <TableHead>Factura</TableHead>
                <TableHead className="text-right">Importe</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {movimientos.map((m) => (
                <TableRow key={String(m.movimiento_id)}>
                  <TableCell>{typeof m.fecha === 'string' ? formatDate(m.fecha) : '—'}</TableCell>
                  <TableCell>{String(m.tipo)}</TableCell>
                  <TableCell>{String(m.referencia)}</TableCell>
                  <TableCell className="text-right">{importe(m.abono)}</TableCell>
                  <TableCell className="text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() =>
                        setObjetivo({
                          modo: 'reversa',
                          id: String(m.movimiento_id),
                          texto: `${String(m.tipo)} de ${importe(m.abono)} sobre ${String(m.referencia)}`,
                        })
                      }
                    >
                      Revertir
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <p className="text-xs text-ink-muted">
            Los pagos con complemento de pago (REP) no se revierten aquí: se corrigen cancelando el REP.
          </p>
        </div>
      )}

      {objetivo && (
        <CorreccionCarteraDialog
          key={`${objetivo.modo}-${objetivo.id}`}
          open
          onOpenChange={(abierto) => !abierto && setObjetivo(null)}
          modo={objetivo.modo}
          objetivo={objetivo.texto}
          plazoActualDias={objetivo.plazoActualDias}
          isPending={enCurso}
          onConfirm={(datos) => void confirmar(datos)}
        />
      )}
    </section>
  );
}
