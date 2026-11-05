import { Fragment, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { DateTimeDisplay, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import {
  useAlmCompraEnvios,
  useReintentarAlmCompraEnvio,
  type AlmCompraEnvio,
} from '@/features/compras/integraciones/api/useAlmCompraEnvios';

const ESTADOS = ['PENDIENTE', 'ENVIADO', 'PROCESADO', 'DUPLICADO', 'ERROR', 'RECHAZADO'] as const;
const VARIANTE: Record<AlmCompraEnvio['estado'], 'success' | 'warning' | 'danger' | 'info'> = {
  PENDIENTE: 'warning', ENVIADO: 'info', PROCESADO: 'success', DUPLICADO: 'success', ERROR: 'danger', RECHAZADO: 'warning',
};
const ETIQUETA_TIPO: Record<AlmCompraEnvio['tipo'], string> = {
  COMPRA_RECIBIDA: 'Compra (OC autorizada)',
  COMPRA_MODIFICADA: 'Compra modificada / entrada',
  COMPRA_CANCELADA: 'Cancelación',
};

/** Qué recibe A+W de este mensaje: hojas, lote y rack (CA3.10) y, en importación, pedimento, BL, aduana y fracción (CA12.2). */
function Detalle({ e }: { e: AlmCompraEnvio }) {
  const d = e.datos;
  if (!d) return <p className="text-sm text-ink-muted">El mensaje no se pudo armar: {e.ultimoError}</p>;
  if (e.tipo === 'COMPRA_CANCELADA') return <p className="text-sm">Cancelar la compra {d.folio}{d.motivo ? `. Motivo: ${d.motivo}` : ''}.</p>;
  return (
    <div className="flex flex-col gap-3 text-sm">
      {d.proveedor && <p>Proveedor A+W {d.proveedor.claveAw} · {d.proveedor.nombre}</p>}
      <Table aria-label="Hojas que recibe A+W">
        <TableHeader>
          <TableRow><TableHead>Línea</TableHead><TableHead>Artículo A+W</TableHead><TableHead>Medida (mm)</TableHead>
            <TableHead className="text-right">Hojas</TableHead><TableHead>Lote</TableHead><TableHead>Rack</TableHead><TableHead>Pedimento</TableHead></TableRow>
        </TableHeader>
        <TableBody>
          {(d.lineas ?? []).map((l) => (
            <TableRow key={`c${l.lineaErp}`}>
              <TableCell>{l.lineaErp}</TableCell><TableCell>{l.articuloAw}</TableCell>
              <TableCell>{l.ancho && l.alto ? `${l.ancho} × ${l.alto}` : '—'}</TableCell>
              <TableCell className="text-right tabular-nums">{l.piezas}</TableCell>
              <TableCell>{l.lote ?? '—'}</TableCell><TableCell className="text-ink-muted">al recibir</TableCell>
              <TableCell>{l.pedimento ?? '—'}</TableCell>
            </TableRow>
          ))}
          {(d.recepciones ?? []).flatMap((r) => r.lineas.map((l, i) => (
            <TableRow key={`r${r.folio}${l.lineaErp}${i}`} data-testid="fila-recepcion">
              <TableCell>{l.lineaErp}</TableCell><TableCell>Entrada {r.folio}</TableCell><TableCell>—</TableCell>
              <TableCell className="text-right tabular-nums">{l.piezas}</TableCell>
              <TableCell>{l.lote ?? '—'}</TableCell><TableCell>{l.rack ?? '—'}</TableCell>
              <TableCell>{r.importacion?.pedimento ?? '—'}</TableCell>
            </TableRow>
          )))}
        </TableBody>
      </Table>
      {(d.recepciones ?? []).filter((r) => r.importacion).map((r) => (
        <p key={r.folio} data-testid="importacion">
          Entrada {r.folio} · importación: BL {r.importacion!.bl} · contenedor {r.importacion!.contenedor} · aduana {r.importacion!.aduana} ·
          clave de pedimento {r.importacion!.clavePedimento} · fracción {r.lineas.map((l) => l.fraccion).filter(Boolean).join(', ') || '—'}
        </p>
      ))}
    </div>
  );
}

export function TableroEnviosAlmacenes() {
  const [estado, setEstado] = useState('');
  const [abierto, setAbierto] = useState<string | null>(null);
  const q = useAlmCompraEnvios(estado || undefined);
  const reintentar = useReintentarAlmCompraEnvio();
  const puedeReintentar = useHasPermission(PermisosCanonicos.ComprasOrdenesLogistica);

  function onReintentar(e: AlmCompraEnvio) {
    reintentar.mutate({ id: e.id }, {
      onSuccess: () => toast.success(`Se reintentará ${e.mensajeId}.`),
      onError: (err) => toast.error(esApiError(err) ? err.problem.detail ?? err.message : 'No se pudo reintentar el mensaje.'),
    });
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeader titulo="Envíos a Almacenes (A+W)" />
      <p className="text-sm text-ink-muted">
        Lo que el ERP manda a Almacenes por cada compra de materia prima y la respuesta que dio. Un mensaje con error se reintenta
        sin duplicar la compra (el identificador del mensaje es único).
      </p>
      <div className="flex items-center gap-2">
        <label htmlFor="filtro-estado" className="text-sm">Estado</label>
        <select id="filtro-estado" value={estado} onChange={(ev) => setEstado(ev.target.value)}
          className="flex h-ctl-xl rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand">
          <option value="">Todos</option>
          {ESTADOS.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
      {q.isLoading && <TableSkeleton rows={6} columns={[{ width: 'w-24' }, { width: 'w-40' }, { width: 'w-32' }, { width: 'w-20' }, { width: 'w-16' }, { width: 'w-24' }]} />}
      {q.isError && <ErrorState problem={esApiError(q.error) ? q.error.problem : undefined} onRetry={() => q.refetch()} />}
      {q.data && q.data.length === 0 && <EmptyState title="Sin mensajes a Almacenes" description="Aparecen al autorizar una OC de materia prima." />}
      {q.data && q.data.length > 0 && (
        <Table aria-label="Envíos a Almacenes">
          <TableHeader>
            <TableRow>
              <TableHead>OC</TableHead><TableHead>Mensaje</TableHead><TableHead>Tipo</TableHead><TableHead>Estado</TableHead>
              <TableHead>Intentos</TableHead><TableHead>Compra en Almacenes</TableHead><TableHead>Enviado</TableHead><TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {q.data.map((e) => (
              <Fragment key={e.id}>
                <TableRow>
                  <TableCell><Link to="/compras/ordenes/$id" params={{ id: e.ordenCompraId }} className="text-brand underline">{e.referencia}</Link></TableCell>
                  <TableCell className="font-mono text-xs">{e.mensajeId}</TableCell>
                  <TableCell>{ETIQUETA_TIPO[e.tipo]}</TableCell>
                  <TableCell>
                    <Badge variant={VARIANTE[e.estado]}>{e.estado}</Badge>
                    {e.ultimoError && <p className="mt-1 max-w-xs text-xs text-danger-fg">{e.ultimoError}</p>}
                  </TableCell>
                  <TableCell className="tabular-nums">{e.intentos}</TableCell>
                  <TableCell>{e.compraIdAlmacenes ?? '—'}</TableCell>
                  <TableCell><DateTimeDisplay value={e.enviadoEn} variant="datetime" /></TableCell>
                  <TableCell className="text-right">
                    <div className="flex justify-end gap-2">
                      <Button size="sm" variant="outline" aria-expanded={abierto === e.id}
                        onClick={() => setAbierto(abierto === e.id ? null : e.id)}>
                        {abierto === e.id ? 'Ocultar' : 'Qué recibe A+W'}
                      </Button>
                      {puedeReintentar && e.estado === 'ERROR' && !e.enviadoEn && (
                        <Button size="sm" onClick={() => onReintentar(e)} disabled={reintentar.isPending}>Reintentar</Button>
                      )}
                    </div>
                  </TableCell>
                </TableRow>
                {abierto === e.id && <TableRow><TableCell colSpan={8}><Detalle e={e} /></TableCell></TableRow>}
              </Fragment>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
