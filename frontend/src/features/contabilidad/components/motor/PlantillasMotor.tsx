import { Fragment, useState } from 'react';
import { ChevronDown, ChevronRight, Info } from 'lucide-react';
import { BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { usePlantillasContables } from '../../api/motor';
import type { LineaPlantillaContable, PlantillaContable } from '../../api/motor-types';
import { fechaCorta } from '../../lib/dimensiones';
import { mensajeMotor } from '../../lib/motor';

const TEXTO_AUXILIAR: Record<string, string> = { Ninguno: '—', Proveedor: 'Proveedor', Cliente: 'Cliente' };

/** Consulta de las plantillas del motor por tipo de evento (C1.4). Solo lectura: se mantienen por sistema. */
export function PlantillasMotor() {
  const plantillas = usePlantillasContables();
  const [abiertas, setAbiertas] = useState<ReadonlySet<string>>(new Set());
  const items = plantillas.data ?? [];

  function alternar(id: string) {
    setAbiertas((prev) => {
      const s = new Set(prev);
      if (s.has(id)) s.delete(id);
      else s.add(id);
      return s;
    });
  }

  return (
    <div className="flex flex-col gap-3">
      <p role="note" className="flex items-start gap-2 rounded-md bg-info-bg px-3 py-2.5 text-sm text-info-fg">
        <Info className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
        Las plantillas se mantienen por sistema; lo que cambia Contabilidad son los roles.
      </p>
      <ListadoModulo filtros={<BarraFiltros total={`${items.length} plantillas`} />}>
        {plantillas.isLoading ? (
          <div className="flex flex-col gap-2 p-4" aria-label="Cargando plantillas">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : plantillas.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar las plantillas. {mensajeMotor(plantillas.error)}
            <Button variant="ghost" size="sm" onClick={() => void plantillas.refetch()}>
              Reintentar
            </Button>
          </div>
        ) : items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">No hay plantillas registradas.</p>
        ) : (
          <div className="overflow-x-auto">
            <Table className="w-full min-w-245 text-sm">
              <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="h-9 w-10 px-3">
                    <span className="sr-only">Ver líneas</span>
                  </TableHead>
                  <TableHead className="px-3">Tipo de evento</TableHead>
                  <TableHead className="px-3">Descripción</TableHead>
                  <TableHead className="px-3 text-right">Versión</TableHead>
                  <TableHead className="px-3">Vigente desde</TableHead>
                  <TableHead className="px-3">Tipo de póliza</TableHead>
                  <TableHead className="px-3 text-right">Líneas</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((p) => {
                  const abierta = abiertas.has(p.id);
                  return (
                    <Fragment key={p.id}>
                      <TableRow className="h-12 border-b border-line-row">
                        <TableCell className="px-3">
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-expanded={abierta}
                            aria-controls={`lineas-${p.id}`}
                            aria-label={`${abierta ? 'Ocultar' : 'Ver'} líneas de ${p.tipoEvento}`}
                            onClick={() => alternar(p.id)}
                          >
                            {abierta ? <ChevronDown aria-hidden="true" /> : <ChevronRight aria-hidden="true" />}
                          </Button>
                        </TableCell>
                        <TableCell className="px-3 font-mono text-xs font-medium">{p.tipoEvento}</TableCell>
                        <TableCell className="max-w-100 px-3">
                          <div className="truncate">{p.descripcion}</div>
                          <div className="truncate text-xs text-ink-muted">Concepto: {p.concepto}</div>
                        </TableCell>
                        <TableCell className="px-3 text-right tabular-nums">{p.version}</TableCell>
                        <TableCell className="whitespace-nowrap px-3 text-ink-secondary">{fechaCorta(p.vigenteDesde)}</TableCell>
                        <TableCell className="px-3 font-mono text-xs">{p.tipoOperacionClave}</TableCell>
                        <TableCell className="px-3 text-right tabular-nums">{p.lineas.length}</TableCell>
                      </TableRow>
                      {abierta && (
                        <TableRow id={`lineas-${p.id}`} className="border-b border-line-row bg-surface-subtle">
                          <TableCell colSpan={7} className="px-3 py-3">
                            <LineasPlantilla plantilla={p} />
                          </TableCell>
                        </TableRow>
                      )}
                    </Fragment>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}

function LineasPlantilla({ plantilla }: { plantilla: PlantillaContable }) {
  const lineas = [...plantilla.lineas].sort((a, b) => a.orden - b.orden);
  return (
    <Table aria-label={`Líneas de ${plantilla.tipoEvento}`} className="w-full text-sm">
      <TableHeader className="static bg-transparent text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
        <TableRow>
          <TableHead className="h-8 px-2 text-right">#</TableHead>
          <TableHead className="px-2">Rol</TableHead>
          <TableHead className="px-2">Lado</TableHead>
          <TableHead className="px-2">Importe</TableHead>
          <TableHead className="px-2">Auxiliar</TableHead>
          <TableHead className="px-2">Alcance</TableHead>
          <TableHead className="px-2">Centro de costo</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {lineas.map((l) => (
          <TableRow key={l.orden} className="h-9 border-b border-line-row last:border-b-0">
            <TableCell className="px-2 text-right tabular-nums text-ink-muted">{l.orden}</TableCell>
            <TableCell className="px-2 font-mono text-xs">{l.rol}</TableCell>
            <TableCell className="px-2">
              <Badge variant={l.lado === 'Cargo' ? 'info' : 'neutral'}>{l.lado}</Badge>
            </TableCell>
            <TableCell className="px-2 font-mono text-xs">{l.campo}</TableCell>
            <TableCell className="px-2 text-ink-secondary">{TEXTO_AUXILIAR[l.auxiliar] ?? l.auxiliar}</TableCell>
            <TableCell className="px-2 text-ink-secondary">{alcance(l)}</TableCell>
            <TableCell className="px-2 text-ink-secondary">{l.conCentroCosto ? 'Sí' : 'No'}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function alcance(l: LineaPlantillaContable): string {
  if (!l.porLinea) return 'Documento';
  if (l.soloServicios === true) return 'Por partida · solo servicios';
  if (l.soloServicios === false) return 'Por partida · solo bienes';
  return 'Por partida';
}
