import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DateTimeDisplay } from '@/components/erp';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import {
  EstadoOrdenCompra,
  TipoRespuestaProveedorOc,
  type OrdenCompraDetalleResponse,
} from '../api/types';
import { useRegistrarRespuestaProveedor } from '../api/useRegistrarRespuestaProveedor';
import { EstadoComercialProveedorBadge } from './EstadoComercialProveedorBadge';
import { RegistrarRespuestaProveedorSheet } from './RegistrarRespuestaProveedorSheet';

/** F1-COM-11: panel «Respuesta del proveedor» del detalle de OC (estado, compromiso vigente, historial). */
export function RespuestaProveedorPanel({ oc }: { oc: OrdenCompraDetalleResponse }) {
  const usuarios = useUsuarios();
  const nombres = mapById(usuarios.data?.items);
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor,
  );
  const mutation = useRegistrarRespuestaProveedor();
  const [abierto, setAbierto] = useState(false);
  const [errorServidor, setErrorServidor] = useState<string | null>(null);
  const respuestas = oc.respuestasProveedor ?? [];
  const autorizada = oc.estado === EstadoOrdenCompra.Autorizada;
  if (!autorizada && respuestas.length === 0) return null;
  const nombre = (id: string) => nombres.get(id)?.nombre ?? id;
  const posicion = (lineaId: string) => oc.lineas.find((l) => l.id === lineaId)?.posicion ?? 0;

  return (
    <section
      aria-label="Respuesta del proveedor"
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <h2 className="text-md font-semibold">Respuesta del proveedor</h2>
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <EstadoComercialProveedorBadge estado={oc.estadoComercialProveedor ?? 0} />
        {oc.fechaCompromisoProveedor && (
          <span>
            Compromiso vigente: <DateTimeDisplay value={oc.fechaCompromisoProveedor} />
          </span>
        )}
      </div>
      <p className="text-xs text-ink-muted">
        La respuesta no registra recepción ni cambia cantidades: recepción, factura y pago avanzan
        por separado.
      </p>
      {puedeRegistrar && (
        <Button
          variant="secondary"
          disabled={!autorizada}
          title={autorizada ? undefined : 'Solo se registra la respuesta en una orden autorizada.'}
          onClick={() => {
            setErrorServidor(null);
            setAbierto(true);
          }}
        >
          Registrar respuesta del proveedor
        </Button>
      )}
      {respuestas.map((r) => (
        <div key={r.id} className="space-y-1 border-t border-line-divider pt-3 text-sm">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium">Respuesta {r.numero}</span>
            <Badge variant={r.tipo == null ? 'neutral' : r.tipo === TipoRespuestaProveedorOc.Confirmacion ? 'info' : 'danger'}>
              {r.tipo == null ? 'Sin clasificar' : r.tipo === TipoRespuestaProveedorOc.Confirmacion ? 'Confirmación' : 'Rechazo'}
            </Badge>
            <Badge variant={r.vigente ? 'success' : 'neutral'}>
              {r.vigente ? 'Vigente' : 'Reemplazada'}
            </Badge>
            {r.tieneDiferencias && <Badge variant="warning">Con diferencias</Badge>}
          </div>
          <p className="text-ink-secondary">
            Fecha de respuesta: {r.fechaRespuesta ? <DateTimeDisplay value={r.fechaRespuesta} /> : 'Sin fecha'} · Registró{' '}
            {nombre(r.registradaPorId)}{' '}
            <DateTimeDisplay value={r.registradaEn} variant="datetime" />
          </p>
          {r.contactoProveedor && (
            <p className="text-ink-secondary">Contacto: {r.contactoProveedor}</p>
          )}
          {r.evidencia && <p className="text-ink-secondary">Referencia: {r.evidencia}</p>}
          {r.motivo && (
            <p className="text-ink-secondary">
              {r.tipo === TipoRespuestaProveedorOc.Rechazo ? 'Motivo' : 'Comentario'}: {r.motivo}
            </p>
          )}
          {r.compromisos.length > 0 && (
            <Table className="w-full max-w-md text-left text-xs">
              <TableHeader>
                <TableRow className="text-ink-muted">
                  <TableHead scope="col" className="py-1 font-medium">
                    Partida
                  </TableHead>
                  <TableHead scope="col" className="py-1 text-right font-medium">
                    Cantidad
                  </TableHead>
                  <TableHead scope="col" className="py-1 pl-4 font-medium">
                    Fecha prometida
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody className="[&_tr]:h-12">
                {[...r.compromisos]
                  .sort(
                    (a, b) =>
                      posicion(a.lineaOrdenCompraId) - posicion(b.lineaOrdenCompraId) ||
                      a.fechaPrometida.localeCompare(b.fechaPrometida),
                  )
                  .map((c) => (
                    <TableRow key={c.id}>
                      <TableCell className="py-1">
                        Partida {posicion(c.lineaOrdenCompraId) || '—'}
                      </TableCell>
                      <TableCell className="py-1 text-right tabular-nums">{c.cantidad}</TableCell>
                      <TableCell className="py-1 pl-4">
                        <DateTimeDisplay value={c.fechaPrometida} />
                      </TableCell>
                    </TableRow>
                  ))}
              </TableBody>
            </Table>
          )}
          <details open={r.vigente}>
            <summary className="cursor-pointer text-sm text-ink-secondary">
              Archivos de evidencia
            </summary>
            <DocumentoAdjuntosSection
              base={`/api/v1/compras/ordenes/respuestas-proveedor/${r.id}`}
              tipo="oc_respuesta_proveedor"
              permiso="compras.ordenes"
              permisos={{
                ver: PermisosCanonicos.ComprasOrdenesLeer,
                subir: PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor,
              }}
            />
          </details>
        </div>
      ))}
      <RegistrarRespuestaProveedorSheet
        oc={oc}
        open={abierto}
        onOpenChange={setAbierto}
        isPending={mutation.isPending}
        errorServidor={errorServidor}
        onSubmit={async (command) => {
          try {
            await mutation.mutateAsync({
              ordenCompraId: oc.id,
              command,
              idempotencyKey: crypto.randomUUID(),
            });
            toast.success('Respuesta del proveedor registrada.');
            setAbierto(false);
          } catch (error) {
            setErrorServidor(
              esApiError(error)
                ? (error.problem.detail ?? error.problem.title)
                : 'No se pudo registrar la respuesta. Vuelve a intentarlo.',
            );
          }
        }}
      />
    </section>
  );
}
