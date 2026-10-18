import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useMaterialEnRevision } from '@/features/almacen/api/useMaterialEnRevision';
import {
  ResultadoDictamenLabels,
  type MaterialEnRevisionItem,
} from '@/features/almacen/api/types';
import { DictamenRevisionSheet } from '@/features/almacen/components/DictamenRevisionSheet';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

function PlazoBadge({ item }: { item: MaterialEnRevisionItem }) {
  if (item.vencida) return <Badge variant="danger">Vencido</Badge>;
  if (item.cantidadPendiente === 0) return <Badge variant="success">Dictaminado</Badge>;
  if (item.plazoNoCalculable) return <Badge variant="neutral">Sin calendario</Badge>;
  return <Badge variant="warning">En plazo</Badge>;
}

/**
 * ALM-04 — Bandeja «Material en revisión». Líneas dañadas de recepciones, con pendiente de dictamen,
 * plazo (5 días hábiles) y evidencia por línea (servicio genérico de adjuntos G1.2).
 */
export function MaterialEnRevisionPage() {
  const [soloPendientes, setSoloPendientes] = useState(true);
  const [q, setQ] = useState<string | undefined>(undefined);
  const [evidencia, setEvidencia] = useState<MaterialEnRevisionItem | null>(null);
  const [dictaminando, setDictaminando] = useState<MaterialEnRevisionItem | null>(null);
  const puedeDictaminar = useHasPermission(PermisosCanonicos.AlmacenCuarentenaDictaminar);
  const query = useMaterialEnRevision({ soloPendientes, q });
  const items = query.data ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Material en revisión"
        descripcion="Material dañado o en duda que llegó en recepciones. No está disponible hasta que el Gerente de almacén/calidad lo dictamine."
      />

      <Tabs
        value={soloPendientes ? 'pendientes' : 'todos'}
        onValueChange={(v) => setSoloPendientes(v === 'pendientes')}
      >
        <TabsList aria-label="Filtrar por dictamen" className="gap-3 px-1">
          <TabsTrigger value="pendientes" aria-controls={undefined}>
            Pendientes
          </TabsTrigger>
          <TabsTrigger value="todos" aria-controls={undefined}>
            Todos
          </TabsTrigger>
        </TabsList>
      </Tabs>

      <BusquedaFolio
        aria-label="Buscar por folio de recepción o clave de artículo"
        valor={q}
        onCommit={setQ}
        placeholder="Folio de recepción o clave de artículo"
        className="max-w-sm"
      />

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el material en revisión"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-48' },
            { width: 'w-20' },
            { width: 'w-20' },
            { width: 'w-28' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="No hay material en revisión"
          description={
            soloPendientes
              ? 'No hay líneas pendientes de dictamen.'
              : 'Aún no se ha registrado material dañado en recepciones.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          {items.some((i) => i.plazoNoCalculable) && (
            <AvisoEnLinea tono="advertencia">
              Plazo no calculable: falta el calendario de días hábiles. Configúralo para ver la fecha
              límite y los vencimientos del dictamen.
            </AvisoEnLinea>
          )}
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Recepción</TableHead>
                  <TableHead>Fecha</TableHead>
                  <TableHead>Artículo</TableHead>
                  <TableHead className="text-right">En revisión</TableHead>
                  <TableHead className="text-right">Pendiente</TableHead>
                  <TableHead>Plazo</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((i) => (
                  <TableRow key={i.lineaRevisionId} className="hover:bg-surface-subtle">
                    <TableCell className="font-mono text-xs">
                      <Link
                        to="/almacen/recepciones/$id"
                        params={{ id: i.recepcionId }}
                        className="text-brand hover:underline"
                        aria-label={`Abrir recepción ${i.recepcionFolio}`}
                      >
                        {i.recepcionFolio}
                      </Link>
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <DateTimeDisplay value={i.fechaRecepcion} variant="corta" />
                    </TableCell>
                    <TableCell>
                      <div className="text-sm">
                        <span className="font-mono text-xs">{i.articuloClave ?? i.articuloId}</span>
                        {i.articuloDescripcion ? ` · ${i.articuloDescripcion}` : ''}
                      </div>
                      {i.motivoDano && <div className="text-xs text-ink-muted">{i.motivoDano}</div>}
                      {i.dictamenes.map((d) => (
                        <div key={d.id} className="text-xs text-ink-muted">
                          {ResultadoDictamenLabels[d.resultado]} · {d.cantidad} · {d.motivo} ·{' '}
                          <DateTimeDisplay value={d.dictaminadoAt} variant="corta" />
                          {d.devolucionAProveedorId && (
                            <>
                              {' · '}
                              <Link
                                to="/almacen/devoluciones/proveedor/$id"
                                params={{ id: d.devolucionAProveedorId }}
                                className="text-primary underline"
                              >
                                Ver devolución
                              </Link>
                            </>
                          )}
                        </div>
                      ))}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {i.cantidadEnRevision} {i.unidadMedida}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{i.cantidadPendiente}</TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        <DateTimeDisplay
                          value={i.fechaLimite}
                          variant="corta"
                          vencida={i.vencida}
                          empty="—"
                        />
                        <PlazoBadge item={i} />
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap items-center gap-2">
                        <Button variant="outline" size="sm" onClick={() => setEvidencia(i)}>
                          Evidencia
                        </Button>
                        {i.cantidadPendiente > 0 &&
                          (puedeDictaminar ? (
                            <Button size="sm" onClick={() => setDictaminando(i)}>
                              Dictaminar
                            </Button>
                          ) : (
                            <span className="text-xs text-ink-muted">
                              Requiere permiso del Gerente de almacén/calidad
                            </span>
                          ))}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      )}

      <DictamenRevisionSheet
        item={dictaminando}
        onOpenChange={(abierto) => !abierto && setDictaminando(null)}
        onAgregarEvidencia={(fila) => {
          setDictaminando(null);
          setEvidencia(fila);
        }}
      />

      <Sheet open={evidencia !== null} onOpenChange={(abierto) => !abierto && setEvidencia(null)}>
        <SheetContent side="right" size="form">
          <SheetHeader>
            <SheetTitle>Evidencia del daño</SheetTitle>
            <SheetDescription>
              {evidencia ? `Recepción ${evidencia.recepcionFolio} · ${evidencia.articuloClave ?? ''}` : ''}
            </SheetDescription>
          </SheetHeader>
          {evidencia && (
            <div className="p-6">
              <DocumentoAdjuntosSection
                base={`/api/v1/almacen/revision/lineas/${evidencia.lineaRevisionId}`}
                tipo="linea_revision"
                permiso="almacen.entradas"
                permisos={{
                  ver: PermisosCanonicos.AlmacenEntradasLeer,
                  subir: PermisosCanonicos.AlmacenEntradasCapturar,
                  baja: PermisosCanonicos.AlmacenEntradasCapturar,
                }}
              />
            </div>
          )}
        </SheetContent>
      </Sheet>
    </div>
  );
}
