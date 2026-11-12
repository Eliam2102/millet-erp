import { useState } from 'react';
import { toast } from 'sonner';
import { TextAreaField } from '@/components/erp';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useDecidirRoturaApp, useRoturasApp, type RoturaAppPendiente } from '@/features/almacen/api/useSalidasRotura';
import { codigoSoporte, esApiError } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';

interface Grupo { articuloAw?: string; ancho?: number; alto?: number; lote?: string; piezas?: number; m2?: number }
interface Datos { sucursal?: string; motivo?: string; piezas?: number; almacenes?: { grupos?: Grupo[] }[] }

/** Resumen legible del mensaje de la app (el backend manda el JSON crudo). */
function resumen(payload: string) {
  let datos: Datos = {};
  try {
    datos = (JSON.parse(payload) as { datos?: Datos }).datos ?? {};
  } catch {
    /* payload ilegible: se muestran solo los campos del mensaje */
  }
  const grupos = (datos.almacenes ?? []).flatMap((a) => a.grupos ?? []);
  return {
    sucursal: datos.sucursal ?? '—',
    motivo: datos.motivo ?? '—',
    piezas: datos.piezas ?? grupos.reduce((s, g) => s + (g.piezas ?? 0), 0),
    detalle: grupos.map((g) =>
      [g.articuloAw, g.ancho && g.alto ? `${g.ancho}×${g.alto}` : null, g.lote ? `lote ${g.lote}` : null, g.piezas != null ? `${g.piezas} pzas` : null]
        .filter(Boolean)
        .join(' · '),
    ),
  };
}

/** M-223: roturas capturadas en la app de Almacenes; otro usuario del ERP las autoriza (baja saldo) o rechaza. */
export function RoturasAppPorAutorizar() {
  const query = useRoturasApp(true);
  const decidir = useDecidirRoturaApp();
  const [autorizando, setAutorizando] = useState<RoturaAppPendiente | null>(null);
  const [rechazando, setRechazando] = useState<RoturaAppPendiente | null>(null);
  const [motivo, setMotivo] = useState<string | null>(null);
  const items = query.data ?? [];
  if (!query.isError && items.length === 0) return null;

  function resolver(accion: 'autorizar' | 'rechazar', r: RoturaAppPendiente) {
    decidir.mutate(
      { mensajeId: r.mensajeId, accion, motivo: motivo?.trim() },
      {
        onSuccess: (d) => {
          toast.success(accion === 'autorizar' ? `Rotura de la app autorizada${d.folio ? ` (${d.folio})` : ''}: el saldo ya bajó` : 'Rotura de la app rechazada');
          setAutorizando(null);
          setRechazando(null);
          setMotivo(null);
        },
        onError: (e) => {
          toast.error(errorUsuario(e, 'No se pudo guardar la decisión'), { description: esApiError(e) ? codigoSoporte(e) : undefined });
          setAutorizando(null);
        },
      },
    );
  }

  return (
    <section aria-labelledby="roturas-app-titulo" className="space-y-2">
      <h2 id="roturas-app-titulo" className="text-sm font-semibold">
        Roturas de la app por autorizar
      </h2>
      {query.isError ? (
        <p role="alert" className="text-sm text-danger-fg">No se pudieron cargar las roturas de la app.</p>
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Referencia</TableHead>
                  <TableHead>Sucursal</TableHead>
                  <TableHead>Motivo</TableHead>
                  <TableHead className="text-right">Piezas</TableHead>
                  <TableHead>Artículos</TableHead>
                  <TableHead>Recibido</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((r) => {
                  const x = resumen(r.payload);
                  return (
                    <TableRow key={r.mensajeId}>
                      <TableCell className="font-mono text-xs">{r.referencia ?? r.folio ?? r.mensajeId}</TableCell>
                      <TableCell>{x.sucursal}</TableCell>
                      <TableCell>{x.motivo}</TableCell>
                      <TableCell className="text-right tabular-nums">{x.piezas}</TableCell>
                      <TableCell className="text-xs">
                        {x.detalle.map((d, i) => (
                          <div key={i}>{d}</div>
                        ))}
                      </TableCell>
                      <TableCell className="whitespace-nowrap">
                        <DateTimeDisplay value={r.recibidoAt} />
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-2">
                          <Button size="sm" aria-label={`Autorizar rotura de la app ${r.referencia ?? ''}`} onClick={() => setAutorizando(r)}>
                            Autorizar
                          </Button>
                          <Button size="sm" variant="outline" aria-label={`Rechazar rotura de la app ${r.referencia ?? ''}`} onClick={() => setRechazando(r)}>
                            Rechazar
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      )}

      <ConfirmarAccionDialog
        open={autorizando !== null}
        onOpenChange={(abierto) => !abierto && setAutorizando(null)}
        titulo="Autorizar rotura de la app"
        resumen="El saldo baja a costo promedio vigente y se avisa a Contabilidad para la póliza de merma. Debe autorizarla un usuario distinto de quien la capturó."
        verbo="Autorizar rotura"
        tono="primario"
        pendiente={decidir.isPending}
        onConfirmar={() => autorizando && resolver('autorizar', autorizando)}
      />
      <ConfirmarAccionDialog
        open={rechazando !== null}
        onOpenChange={(abierto) => {
          if (!abierto) {
            setRechazando(null);
            setMotivo(null);
          }
        }}
        titulo="Rechazar rotura de la app"
        resumen={
          <div className="space-y-2">
            <p>La rotura queda rechazada y el saldo no cambia. Indica el motivo.</p>
            <TextAreaField value={motivo} onChange={setMotivo} maxLength={500} minRows={2} />
          </div>
        }
        verbo="Rechazar rotura"
        tono="peligro"
        pendiente={decidir.isPending}
        onConfirmar={() => {
          if (!motivo?.trim()) {
            toast.error('Indica el motivo del rechazo.');
            return;
          }
          if (rechazando) resolver('rechazar', rechazando);
        }}
      />
    </section>
  );
}
