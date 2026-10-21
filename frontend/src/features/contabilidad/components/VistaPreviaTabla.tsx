import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { ESTADOS_IMPORTACION } from '../lib/estados-ui';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import type { ErrorFila, VistaPrevia } from '../api/types';
import { nombreColumna, severidadLegible, tituloHallazgo } from '../lib/textos';

function Hallazgo({ e }: { e: ErrorFila }) {
  const esError = e.severidad === 'Error';
  return (
    // El código interno queda en el title (soporte); en pantalla, lenguaje de usuario.
    <li className="space-y-0.5" title={e.codigo}>
      <span className={esError ? 'text-danger-fg' : 'text-warning-fg'}>
        <span className="font-medium">
          {severidadLegible(e.severidad)} · {tituloHallazgo(e.codigo)}
          {e.columna ? ` (columna «${nombreColumna(e.columna)}»)` : ''}:
        </span>{' '}
        {e.mensaje}
      </span>
      {e.sugerencia && (
        <span className="block text-xs text-ink-muted">Qué hacer: {e.sugerencia}</span>
      )}
    </li>
  );
}

/** Vista previa: resumen, hallazgos del archivo, y tabla con la acción por fila y su filtro «solo con errores». */
export function VistaPreviaTabla({
  vp,
  desplazamiento = 0,
}: {
  vp: VistaPrevia;
  desplazamiento?: number;
}) {
  const [soloErrores, setSoloErrores] = useState(false);
  const filas = soloErrores
    ? vp.filas.filter((f) => f.errores.some((e) => e.severidad === 'Error'))
    : vp.filas;
  const pagina = usePaginacionLocal(filas, String(soloErrores) + vp.huella);
  const r = vp.resumen;
  return (
    <div className="space-y-4">
      <dl
        className="grid grid-cols-2 gap-3 rounded-lg bg-surface-card shadow-card-flat p-3 text-sm sm:grid-cols-4"
        aria-label="Resumen de la vista previa"
      >
        {(
          [
            ['Leídas', r.leidas],
            ['Crear', r.crear],
            ['Actualizar', r.actualizar],
            ['Sin cambios', r.sinCambios],
            ['Rechazadas', r.rechazadas],
            ['Errores', r.errores],
            ['Advertencias', r.advertencias],
            ['Vacías', r.vacias],
            ['Títulos omitidos', r.omitidas ?? 0],
          ] as const
        ).map(([k, v]) => (
          <div key={k}>
            <dt className="text-xs text-ink-muted">{k}</dt>
            <dd className="font-medium">{v}</dd>
          </div>
        ))}
      </dl>

      {vp.archivo.length > 0 && (
        <section aria-label="Hallazgos del archivo" className="space-y-1">
          <h3 className="text-sm font-semibold">Hallazgos del archivo</h3>
          <ul className="space-y-1 text-sm">
            {vp.archivo.map((e, i) => (
              <Hallazgo key={i} e={e} />
            ))}
          </ul>
        </section>
      )}

      <ListadoModulo
        filtros={
          <BarraFiltros
            filtros={
              <>
                <Checkbox
                  id="solo-errores"
                  checked={soloErrores}
                  onCheckedChange={(v) => setSoloErrores(v === true)}
                />
                <Label htmlFor="solo-errores" className="text-sm font-normal">
                  Solo con errores
                </Label>
              </>
            }
            total={`${filas.length} filas`}
          />
        }
        pie={
          <PaginacionTabla
            total={filas.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        <Table className="w-full min-w-140 text-sm">
          <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
            <TableRow>
              <TableHead className="px-3 py-2">Fila</TableHead>
              <TableHead className="px-3 py-2">Acción</TableHead>
              <TableHead className="px-3 py-2">Errores y advertencias</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filas.length === 0 && (
              <TableRow>
                <TableCell colSpan={3} className="px-3 py-4 text-ink-muted">
                  Sin filas para mostrar.
                </TableCell>
              </TableRow>
            )}
            {pagina.visibles.map((f) => (
              <TableRow key={f.fila} className="border-b border-line-row align-top">
                <TableCell className="px-3 py-1.5">{f.fila + desplazamiento}</TableCell>
                <TableCell className="px-3 py-1.5">
                  <Badge variant={ESTADOS_IMPORTACION[f.accion]?.tono ?? 'neutral'}>
                    {ESTADOS_IMPORTACION[f.accion]?.texto ?? f.accion}
                  </Badge>
                </TableCell>
                <TableCell className="px-3 py-1.5">
                  {f.errores.length === 0 ? (
                    '—'
                  ) : (
                    <ul className="space-y-1">
                      {f.errores.map((e, i) => (
                        <Hallazgo key={i} e={e} />
                      ))}
                    </ul>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </ListadoModulo>
    </div>
  );
}
