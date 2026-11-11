import { useState } from 'react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton, TextAreaField } from '@/components/erp';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import {
  useCambiarOrigenCapturaMp,
  useOrigenCapturaMp,
  type EventoOrigenCaptura,
  type OrigenCaptura,
} from '@/features/almacen/api/useOrigenCapturaMp';
import { esApiError } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';

const ORIGEN: Record<OrigenCaptura, { texto: string; variant: 'success' | 'neutral' | 'warning' }> = {
  APP: { texto: 'App de Almacenes', variant: 'success' },
  ERP: { texto: 'ERP', variant: 'neutral' },
  AMBOS: { texto: 'En transición (ERP y app)', variant: 'warning' },
};

function pendientes(e: EventoOrigenCaptura): number {
  return e.pendientes.mensajesEnRevision + (e.pendientes.mensajesSinAcuse ?? 0);
}

/**
 * M-335: qué origen captura cada evento de materia prima (recepción, consumo, merma, requisición). El otro origen se rechaza con
 * motivo: la pantalla del ERP no registra lo que ya viene de la app, y la app recibe ERROR si el evento sigue en el ERP. Antes de
 * cambiar se ven los pendientes; con pendientes el cambio no procede. Cada cambio queda en la bitácora y se revierte con otro.
 */
export function OrigenCapturaMpPage() {
  const query = useOrigenCapturaMp();
  const cambiar = useCambiarOrigenCapturaMp();
  const [destino, setDestino] = useState<{ evento: EventoOrigenCaptura; origen: 'ERP' | 'APP' } | null>(null);
  const [motivo, setMotivo] = useState<string | null>('');

  const abrir = (evento: EventoOrigenCaptura, origen: 'ERP' | 'APP') => {
    setMotivo('');
    setDestino({ evento, origen });
  };

  const confirmar = () => {
    if (!destino) return;
    cambiar.mutate(
      { evento: destino.evento.evento, origen: destino.origen, motivo: (motivo ?? '').trim() },
      {
        onSuccess: () => {
          toast.success(`La ${destino.evento.nombre} ahora se captura en ${destino.origen === 'APP' ? 'la app de Almacenes' : 'el ERP'}`);
          setDestino(null);
        },
        onError: (e) => toast.error(errorUsuario(e, 'No se pudo cambiar el origen')),
      },
    );
  };

  const data = query.data;
  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Origen de captura de materia prima"
        descripcion="Define si cada evento de materia prima se captura en el ERP (captura provisional) o llega solo de la app de Almacenes."
      />
      <AvisoEnLinea tono="advertencia">
        El origen que no está habilitado se rechaza: el ERP no registra lo que ya viene de la app y la app recibe ERROR con el motivo.
        Antes de cambiar hay que conciliar los mensajes en revisión y sin acuse. Cada cambio queda en la bitácora y se puede revertir.
      </AvisoEnLinea>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el origen de captura"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || !data ? (
        <TableSkeleton rows={4} columns={[{ width: 'w-28' }, { width: 'w-36' }, { width: 'w-28' }, { width: 'w-24' }]} />
      ) : (
        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Evento</TableHead>
                <TableHead>Se captura en</TableHead>
                <TableHead>Desde</TableHead>
                <TableHead>Pendientes</TableHead>
                <TableHead className="text-right">Cambiar</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.eventos.map((e) => (
                <TableRow key={e.evento}>
                  <TableCell className="font-medium capitalize">{e.nombre}</TableCell>
                  <TableCell>
                    <Badge variant={ORIGEN[e.origen].variant}>{ORIGEN[e.origen].texto}</Badge>
                  </TableCell>
                  <TableCell className="whitespace-nowrap">
                    {e.desde ? <DateTimeDisplay value={e.desde} variant="corta" /> : '—'}
                  </TableCell>
                  <TableCell className="text-sm">
                    {e.pendientes.mensajesEnRevision} en revisión
                    <div className="text-xs text-ink-muted">
                      {e.pendientes.mensajesSinAcuse === null
                        ? 'Sin lectura de la tabla de la app'
                        : `${e.pendientes.mensajesSinAcuse} sin acuse en la app`}
                    </div>
                  </TableCell>
                  <TableCell className="space-x-2 text-right">
                    {e.origen !== 'APP' && (
                      <Button size="sm" onClick={() => abrir(e, 'APP')}>
                        Pasar a la app
                      </Button>
                    )}
                    {e.origen !== 'ERP' && (
                      <Button size="sm" variant="outline" onClick={() => abrir(e, 'ERP')}>
                        {e.origen === 'APP' ? 'Regresar al ERP' : 'Dejar solo en el ERP'}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      <section className="space-y-2">
        <h2 className="text-sm font-semibold text-ink">Bitácora de cambios</h2>
        {data && data.bitacora.length === 0 ? (
          <EmptyState title="Sin cambios" description="Todos los eventos siguen en transición: se aceptan el ERP y la app." />
        ) : (
          data && (
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Fecha</TableHead>
                    <TableHead>Evento</TableHead>
                    <TableHead>De</TableHead>
                    <TableHead>A</TableHead>
                    <TableHead>Motivo</TableHead>
                    <TableHead>Autorizó</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.bitacora.map((c) => (
                    <TableRow key={c.id}>
                      <TableCell className="whitespace-nowrap">
                        <DateTimeDisplay value={c.cambiadoAt} variant="corta" />
                      </TableCell>
                      <TableCell className="capitalize">{data.eventos.find((e) => e.evento === c.evento)?.nombre ?? c.evento}</TableCell>
                      <TableCell>{c.origenAnterior ? ORIGEN[c.origenAnterior].texto : '—'}</TableCell>
                      <TableCell>{ORIGEN[c.origen].texto}</TableCell>
                      <TableCell>{c.motivo}</TableCell>
                      <TableCell className="whitespace-nowrap">{c.cambiadoPor}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )
        )}
      </section>

      <ConfirmarAccionDialog
        open={destino !== null}
        onOpenChange={(abierto) => !abierto && setDestino(null)}
        titulo={destino?.origen === 'APP' ? 'Pasar a la app de Almacenes' : 'Capturar en el ERP'}
        resumen={
          destino && (
            <div className="space-y-3">
              <p>
                La {destino.evento.nombre} de materia prima se capturará solo en{' '}
                {destino.origen === 'APP' ? 'la app de Almacenes; el ERP dejará de registrarla.' : 'el ERP; la app recibirá ERROR.'}
              </p>
              {pendientes(destino.evento) > 0 && (
                <AvisoEnLinea tono="advertencia">
                  Hay {destino.evento.pendientes.mensajesEnRevision} mensaje(s) en revisión
                  {destino.evento.pendientes.mensajesSinAcuse ? ` y ${destino.evento.pendientes.mensajesSinAcuse} sin acuse` : ''}.
                  Concílialos en «Mensajes de la app» antes de cambiar.
                </AvisoEnLinea>
              )}
              <label className="block text-sm font-medium text-ink" htmlFor="motivo-origen">
                Motivo del cambio
              </label>
              <TextAreaField
                value={motivo}
                onChange={setMotivo}
                maxLength={500}
                minRows={2}
                textareaProps={{ id: 'motivo-origen' }}
              />
            </div>
          )
        }
        verbo={destino?.origen === 'APP' ? 'Pasar a la app' : 'Capturar en el ERP'}
        tono="primario"
        onConfirmar={() => {
          if (!(motivo ?? '').trim()) {
            toast.error('Escribe el motivo del cambio');
            return;
          }
          confirmar();
        }}
        pendiente={cambiar.isPending}
      />
    </div>
  );
}
