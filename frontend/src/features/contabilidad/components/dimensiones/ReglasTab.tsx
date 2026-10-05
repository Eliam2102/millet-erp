import { useState } from 'react';
import { Plus } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { hoyLocalISO } from '@/lib/datetime';
import { useCerrarRegla, useEliminarRegla, useReglas, useTiposDocumento } from '../../api/dimensiones';
import type { Regla } from '../../api/dimensiones-types';
import {
  ETIQUETA_DIMENSION, ETIQUETA_REQUERIMIENTO, VARIANTE_ESTADO_REGLA, VARIANTE_REQUERIMIENTO, fechaCorta, mensajeError, vigencia,
} from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';
import { CuentaSelector, type CuentaOpcion } from './CuentaSelector';
import { MatrizEfectiva } from './MatrizEfectiva';
import { ReglaSheet } from './ReglaSheet';

const LIMITE = 10;

/** Reglas cuenta × tipo de documento × dimensión con su vigencia. Configurar exige `contabilidad.dimensiones.administrar`. */
export function ReglasTab({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  const [tipoId, setTipoId] = useState('');
  const [fecha, setFecha] = useState('');
  const [soloPrueba, setSoloPrueba] = useState(false);
  const [offset, setOffset] = useState(0);
  const [sheet, setSheet] = useState<{ open: boolean; regla: Regla | null }>({ open: false, regla: null });
  const [cerrando, setCerrando] = useState<Regla | null>(null);
  const [eliminando, setEliminando] = useState<Regla | null>(null);
  const tipos = useTiposDocumento();
  const reglas = useReglas({
    cuentaId: cuenta?.id, tipoDocumentoId: tipoId || undefined, vigentesA: fecha || undefined,
    esPrueba: soloPrueba || undefined, offset, limit: LIMITE,
  });

  function filtro<T>(set: (v: T) => void) {
    return (v: T) => { set(v); setOffset(0); };
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3" data-print="hidden">
        <div className="flex w-80 flex-col gap-1.5">
          <Label htmlFor="f-cuenta">Cuenta o rama</Label>
          <CuentaSelector id="f-cuenta" value={cuenta} onChange={filtro(setCuenta)} vacio="Todas las cuentas" />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="f-tipo">Tipo de documento</Label>
          <select id="f-tipo" className={SELECT_CLASS} value={tipoId} onChange={(e) => filtro(setTipoId)(e.target.value)}>
            <option value="">Todos</option>
            {(tipos.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.clave} — {t.nombre}</option>)}
          </select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="f-fecha">Vigentes al <span className="font-normal text-ink-muted">(opcional)</span></Label>
          <Input id="f-fecha" type="date" className="w-44" value={fecha} onChange={(e) => filtro(setFecha)(e.target.value)} />
        </div>
        <div className="flex h-ctl-xl items-center gap-2">
          <Checkbox id="f-prueba" checked={soloPrueba} onCheckedChange={(v) => filtro(setSoloPrueba)(v === true)} />
          <Label htmlFor="f-prueba" className="font-normal">Solo reglas de prueba</Label>
        </div>
        {puedeAdministrar && (
          <Button className="ml-auto" onClick={() => setSheet({ open: true, regla: null })}>
            <Plus className="mr-1 size-4" aria-hidden="true" />Nueva regla
          </Button>
        )}
      </div>

      {cuenta && <MatrizEfectiva cuentaId={cuenta.id} tipoDocumentoId={tipoId} fecha={fecha || hoyLocalISO()} />}

      <div className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
        {reglas.isLoading ? (
          <div className="space-y-2 p-4" aria-label="Cargando reglas"><Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" /></div>
        ) : reglas.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar las reglas.
            <Button variant="ghost" size="sm" onClick={() => void reglas.refetch()}>Reintentar</Button>
          </div>
        ) : reglas.data!.items.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            No hay reglas para estos filtros. Sin regla, cada dimensión es opcional (configuración actual) hasta que Contabilidad defina su política.
          </p>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[880px] text-sm">
                <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                  <tr>
                    <th className="px-3 py-2">Cuenta</th><th className="px-3 py-2">Tipo de documento</th><th className="px-3 py-2">Dimensión</th>
                    <th className="px-3 py-2">Requerimiento</th><th className="px-3 py-2">Vigencia</th><th className="px-3 py-2">Estado</th>
                    {puedeAdministrar && <th className="px-3 py-2"><span className="sr-only">Acciones</span></th>}
                  </tr>
                </thead>
                <tbody>
                  {reglas.data!.items.map((r) => (
                    <tr key={r.id} className="border-b border-line-row">
                      <td className="px-3 py-2">
                        <span className="font-mono text-xs">{r.cuentaCodigo}</span>
                        <span className="block text-xs text-ink-muted">{r.cuentaNombre}</span>
                      </td>
                      <td className="px-3 py-2">{r.tipoDocumentoClave ? `${r.tipoDocumentoClave} — ${r.tipoDocumentoNombre}` : 'Todos los tipos'}</td>
                      <td className="px-3 py-2">{ETIQUETA_DIMENSION[r.dimension]}</td>
                      <td className="px-3 py-2"><Badge variant={VARIANTE_REQUERIMIENTO[r.requerimiento]}>{ETIQUETA_REQUERIMIENTO[r.requerimiento]}</Badge></td>
                      <td className="px-3 py-2 text-ink-secondary">{vigencia(r.vigenteDesde, r.vigenteHasta)}</td>
                      <td className="px-3 py-2">
                        <span className="flex flex-wrap gap-1">
                          <Badge variant={VARIANTE_ESTADO_REGLA[r.estado]}>{r.estado}</Badge>
                          {r.esPrueba && <Badge variant="warning" title="Regla de prueba: no es política confirmada por Contabilidad">Prueba</Badge>}
                          {r.usada && <Badge variant="neutral" title={`Ya validó movimientos (el más reciente con fecha contable ${fechaCorta(r.ultimaFechaUso)})`}>Usada</Badge>}
                        </span>
                      </td>
                      {puedeAdministrar && (
                        <td className="px-3 py-2 text-right">
                          <span className="flex justify-end gap-1">
                            <Button
                              variant="outline" size="sm" disabled={!r.editable}
                              title={r.editable ? undefined : 'Ya validó movimientos: ciérrala y crea una regla nueva para cambiar la política'}
                              onClick={() => setSheet({ open: true, regla: r })}
                            >
                              Editar
                            </Button>
                            {r.editable && <Button variant="secondary-danger" size="sm" onClick={() => setEliminando(r)}>Eliminar</Button>}
                            {r.estado !== 'Cerrada' && <Button variant="outline" size="sm" onClick={() => setCerrando(r)}>Cerrar vigencia</Button>}
                          </span>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="flex h-11 items-center justify-between border-t border-line-divider px-4 text-xs text-ink-muted" data-print="hidden">
              <span>{offset + 1}–{offset + reglas.data!.items.length} de {reglas.data!.total}</span>
              <span className="flex gap-2">
                <Button variant="outline" size="sm" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - LIMITE))}>Anterior</Button>
                <Button variant="outline" size="sm" disabled={offset + reglas.data!.items.length >= reglas.data!.total} onClick={() => setOffset(offset + LIMITE)}>Siguiente</Button>
              </span>
            </div>
          </>
        )}
      </div>

      {puedeAdministrar && (
        <>
          {/* Montado solo abierto y con key: el formulario toma su estado inicial de la regla elegida. */}
          {sheet.open && (
            <ReglaSheet key={sheet.regla?.id ?? 'nueva'} open regla={sheet.regla} onOpenChange={(o) => setSheet((s) => ({ ...s, open: o }))} />
          )}
          <CerrarVigenciaDialog regla={cerrando} onClose={() => setCerrando(null)} />
          <EliminarReglaDialog regla={eliminando} onClose={() => setEliminando(null)} />
        </>
      )}
    </div>
  );
}

function CerrarVigenciaDialog({ regla, onClose }: { regla: Regla | null; onClose: () => void }) {
  const cerrar = useCerrarRegla();
  const [hasta, setHasta] = useState(hoyLocalISO());
  const [error, setError] = useState('');
  // No antes de hoy (regla en vigor), de su inicio (regla futura) ni de la última fecha contable que validó.
  const minimo = [hoyLocalISO(), regla?.vigenteDesde ?? '', regla?.ultimaFechaUso ?? ''].sort().at(-1)!;

  function confirmar() {
    if (!regla) return;
    setError('');
    cerrar.mutate({ regla, vigenteHasta: hasta }, { onSuccess: onClose, onError: (e) => setError(mensajeError(e)) });
  }

  return (
    <Dialog open={!!regla} onOpenChange={(o) => { if (!o) { onClose(); setError(''); } }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Cerrar vigencia</DialogTitle>
          <DialogDescription>
            La regla aplicará hasta la fecha que indiques. Los movimientos ya registrados conservan la regla con la que se validaron.
            Para cambiar la política, crea después una regla nueva que empiece al día siguiente.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cerrar-hasta">Vigente hasta</Label>
          <Input id="cerrar-hasta" type="date" min={minimo} value={hasta} onChange={(e) => setHasta(e.target.value)} />
        </div>
        {error && <p role="alert" className="text-sm text-danger-fg">{error}</p>}
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button onClick={confirmar} disabled={cerrar.isPending}>{cerrar.isPending ? 'Cerrando…' : 'Cerrar vigencia'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function EliminarReglaDialog({ regla, onClose }: { regla: Regla | null; onClose: () => void }) {
  const eliminar = useEliminarRegla();
  const [error, setError] = useState('');
  return (
    <Dialog open={!!regla} onOpenChange={(o) => { if (!o) { onClose(); setError(''); } }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Eliminar regla</DialogTitle>
          <DialogDescription>
            {regla && `${ETIQUETA_DIMENSION[regla.dimension]} · ${regla.cuentaCodigo} · ${regla.tipoDocumentoClave ?? 'todos los tipos'}. `}
            Se puede eliminar porque todavía no ha validado ningún movimiento. Esta acción no se puede deshacer.
          </DialogDescription>
        </DialogHeader>
        {error && <p role="alert" className="text-sm text-danger-fg">{error}</p>}
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button
            variant="secondary-danger"
            disabled={eliminar.isPending}
            onClick={() => regla && eliminar.mutate(regla, { onSuccess: onClose, onError: (e) => setError(mensajeError(e)) })}
          >
            {eliminar.isPending ? 'Eliminando…' : 'Eliminar regla'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
