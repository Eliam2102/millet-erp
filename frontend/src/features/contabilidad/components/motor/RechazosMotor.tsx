import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { FileJson, RotateCw, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { formatDateTime } from '@/lib/datetime';
import { useRechazosContables, useReprocesarRechazo } from '../../api/motor';
import { ESTADOS_RECHAZO, type EstadoRechazo, type RechazoContable } from '../../api/motor-types';
import { ESTADO_RECHAZO_UI, VARIANTE_CAUSA, mensajeMotor, textoCausa, type MotorSearch } from '../../lib/motor';
import { usePaginacionLocal } from '../../lib/use-paginacion-local';
import { TabsContabilidad } from '../TabsContabilidad';
import { DescartarRechazoDialog } from './DescartarRechazoDialog';
import { PayloadRechazoSheet } from './PayloadRechazoSheet';

const VISTAS = ESTADOS_RECHAZO.map((estado) => ({ estado, label: ESTADO_RECHAZO_UI[estado].texto }));

interface Props {
  estado: MotorSearch['estado'];
  onEstado: (estado: MotorSearch['estado']) => void;
  puedeGestionar: boolean;
}

/**
 * Eventos que el motor no pudo convertir en póliza (C1.4): causa, detalle e intentos. Con permiso se reprocesan (tras
 * corregir el rol, abrir el periodo, etc.) o se descartan con motivo.
 */
export function RechazosMotor({ estado, onEstado, puedeGestionar }: Props) {
  /** Sin filtro en la URL = Pendiente; «Todos» quita el filtro. */
  const filtro: EstadoRechazo | undefined = estado === 'Todos' ? undefined : (estado ?? 'Pendiente');
  const rechazos = useRechazosContables(filtro);
  const reprocesar = useReprocesarRechazo();
  const keyFor = useBodyScopedIdempotencyKey();
  const [descartando, setDescartando] = useState<RechazoContable | null>(null);
  const [viendo, setViendo] = useState<RechazoContable | null>(null);
  const items = [...(rechazos.data ?? [])].sort((a, b) => b.ultimoIntentoEn.localeCompare(a.ultimoIntentoEn));
  const pagina = usePaginacionLocal(items, filtro ?? '');

  function onReprocesar(r: RechazoContable) {
    const body = { id: r.id, version: r.version };
    reprocesar.mutate(
      { id: r.id, idempotencyKey: keyFor(body) },
      {
        onSuccess: (res) => {
          if (res.generada && res.poliza) {
            toast.success(
              res.poliza.folio ? `Póliza ${res.poliza.folio} generada.` : 'Póliza generada en borrador.',
            );
          } else {
            toast.error(
              `Sigue sin generarse: ${textoCausa(res.causa) || 'rechazo'}.${res.mensaje ? ` ${res.mensaje}` : ''}`,
            );
          }
        },
        onError: (e) => toast.error(mensajeMotor(e)),
      },
    );
  }

  return (
    <>
      <ListadoModulo
        tabs={
          <TabsContabilidad
            estados={VISTAS}
            valor={filtro}
            onChange={(e) => onEstado(e === undefined ? 'Todos' : e === 'Pendiente' ? undefined : e)}
          />
        }
        filtros={<BarraFiltros total={`${items.length} rechazos`} />}
        pie={
          <PaginacionTabla total={items.length} offset={pagina.offset} limit={10} tamanos={[]} onChange={pagina.onChange} />
        }
      >
        {rechazos.isLoading ? (
          <div className="flex flex-col gap-2 p-4" aria-label="Cargando rechazos">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : rechazos.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar los rechazos. {mensajeMotor(rechazos.error)}
            <Button variant="ghost" size="sm" onClick={() => void rechazos.refetch()}>
              Reintentar
            </Button>
          </div>
        ) : items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            {filtro === 'Pendiente'
              ? 'No hay eventos pendientes: todo lo recibido generó su póliza.'
              : 'Ningún rechazo en este estado.'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <Table className="w-full min-w-245 text-sm">
              <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="h-9 px-3">Último intento</TableHead>
                  <TableHead className="px-3">Tipo de evento</TableHead>
                  <TableHead className="px-3">Causa</TableHead>
                  <TableHead className="px-3">Detalle</TableHead>
                  <TableHead className="px-3 text-right">Intentos</TableHead>
                  <TableHead className="px-3">Estado</TableHead>
                  <TableHead className="px-3 text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {pagina.visibles.map((r) => {
                  const enCurso = reprocesar.isPending && reprocesar.variables?.id === r.id;
                  const estadoUi = ESTADO_RECHAZO_UI[r.estado];
                  return (
                    <TableRow key={r.id} className="h-12 border-b border-line-row">
                      <TableCell className="whitespace-nowrap px-3 text-ink-secondary">
                        {formatDateTime(r.ultimoIntentoEn)}
                      </TableCell>
                      <TableCell className="px-3 font-mono text-xs">{r.tipoEvento}</TableCell>
                      <TableCell className="px-3">
                        <Badge variant={VARIANTE_CAUSA[r.causa] ?? 'neutral'}>{textoCausa(r.causa)}</Badge>
                      </TableCell>
                      <TableCell className="max-w-100 px-3">
                        <div className="truncate" title={r.mensaje}>
                          {r.mensaje}
                        </div>
                        {r.detalle && <div className="truncate font-mono text-xs text-ink-muted">{r.detalle}</div>}
                      </TableCell>
                      <TableCell className="px-3 text-right tabular-nums">{r.intentos}</TableCell>
                      <TableCell className="px-3">
                        <div className="flex flex-col items-start gap-0.5">
                          <Badge variant={estadoUi?.variante ?? 'neutral'}>{estadoUi?.texto ?? r.estado}</Badge>
                          {r.estado === 'Reprocesado' && r.polizaId && (
                            <Link
                              to="/contabilidad/polizas/$id"
                              params={{ id: r.polizaId }}
                              search={{}}
                              className="text-xs font-medium text-brand hover:underline"
                            >
                              Ver póliza
                            </Link>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="px-3">
                        <div className="flex justify-end gap-1" data-print="hidden">
                          {puedeGestionar && r.estado === 'Pendiente' && (
                            <>
                              <Button
                                variant="secondary"
                                size="sm"
                                disabled={reprocesar.isPending}
                                onClick={() => onReprocesar(r)}
                                aria-label={`Reprocesar ${r.tipoEvento}`}
                              >
                                <RotateCw aria-hidden="true" />
                                {enCurso ? 'Reprocesando…' : 'Reprocesar'}
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => setDescartando(r)}
                                aria-label={`Descartar ${r.tipoEvento}`}
                              >
                                <XCircle aria-hidden="true" />
                                Descartar
                              </Button>
                            </>
                          )}
                          <Button
                            variant="ghost"
                            size="icon"
                            onClick={() => setViendo(r)}
                            aria-label={`Ver evento de ${r.tipoEvento}`}
                            title="Ver evento"
                          >
                            <FileJson aria-hidden="true" />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      <DescartarRechazoDialog rechazo={descartando} onClose={() => setDescartando(null)} />
      <PayloadRechazoSheet rechazo={viendo} onClose={() => setViendo(null)} />
    </>
  );
}
