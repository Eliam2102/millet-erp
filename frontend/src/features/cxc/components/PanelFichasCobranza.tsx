import { useMemo, useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useUsuarios } from '@/features/catalogos/api';
import {
  useAsignarResponsableFicha,
  useCerrarFicha,
  useCorregirEventoFicha,
  useEscalarFicha,
  useEventosFicha,
  useFichasCobranza,
  useRegistrarGestionFicha,
  useReprogramarFicha,
  type FichaFacturaVencida,
} from '@/features/cxc/api/useFichasCobranza';
import { FichaCobranzaDialog, type DatosFicha, type ModoFicha } from '@/features/cxc/components/FichaCobranzaDialog';

const importe = (v: number) => v.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const mensajeError = (err: unknown) =>
  esApiError(err) ? (err.problem.detail ?? err.problem.title) : err instanceof Error ? err.message : String(err);

const ETIQUETA_PROMESA = { Vigente: 'Promesa vigente', Cumplida: 'Promesa cumplida', Parcial: 'Promesa parcial', Incumplida: 'Promesa incumplida' } as const;

interface Objetivo {
  modo: ModoFicha;
  factura: FichaFacturaVencida;
  eventoId?: string;
  notaActual?: string;
}

/**
 * M-241 (CXC-GESTION): facturas vencidas con su ficha de cobranza — responsable, siguiente revisión, escalamiento y el historial con
 * quién y por qué. Las acciones exigen <c>cobranza.registrar</c>; la lista respeta el alcance por sucursal (ADR-0051).
 */
export function PanelFichasCobranza({ clienteId }: { clienteId: string | null }) {
  const puedeRegistrar = useHasPermission(PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar);
  const [soloSin, setSoloSin] = useState(false);
  const [objetivo, setObjetivo] = useState<Objetivo | null>(null);
  const [historial, setHistorial] = useState<string | null>(null);
  const fichas = useFichasCobranza({ clienteId, soloSinResponsable: soloSin });
  const eventos = useEventosFicha(historial);
  const usuarios = useUsuarios();
  const personas = useMemo(() => (usuarios.data?.items ?? []).filter((u) => u.activo).map((u) => ({ id: u.id, nombre: u.nombre })), [usuarios.data]);

  const asignar = useAsignarResponsableFicha();
  const gestion = useRegistrarGestionFicha();
  const reprogramar = useReprogramarFicha();
  const escalar = useEscalarFicha();
  const corregir = useCorregirEventoFicha();
  const cerrar = useCerrarFicha();
  const pendiente = asignar.isPending || gestion.isPending || reprogramar.isPending || escalar.isPending || corregir.isPending || cerrar.isPending;

  function confirmar(d: DatosFicha) {
    if (!objetivo) return;
    const id = objetivo.factura.facturaCarteraId;
    const opciones = {
      onSuccess: () => {
        toast.success('Ficha actualizada.');
        setObjetivo(null);
      },
      onError: (err: unknown) => toast.error(mensajeError(err)),
    };
    switch (objetivo.modo) {
      case 'responsable':
        asignar.mutate({ facturaCarteraId: id, responsableId: d.personaId!, responsableNombre: d.personaNombre ?? null, motivo: d.motivo }, opciones);
        break;
      case 'gestion':
        gestion.mutate(
          { facturaCarteraId: id, canal: d.canal!, resultado: d.resultado!, nota: d.nota!, montoComprometido: d.monto, fechaComprometida: d.fecha },
          opciones,
        );
        break;
      case 'reprogramar':
        reprogramar.mutate({ facturaCarteraId: id, siguienteRevision: d.fecha!, motivo: d.motivo! }, opciones);
        break;
      case 'escalar':
        escalar.mutate({ facturaCarteraId: id, supervisorId: d.personaId!, supervisorNombre: d.personaNombre ?? null, motivo: d.motivo! }, opciones);
        break;
      case 'cerrar':
        cerrar.mutate({ facturaCarteraId: id, motivo: d.motivoCierre!, explicacionSaldo: d.motivo }, opciones);
        break;
      case 'corregir':
        corregir.mutate({ eventoId: objetivo.eventoId!, notaCorregida: d.nota!, motivo: d.motivo! }, opciones);
        break;
    }
  }

  const filas = fichas.data ?? [];
  return (
    <section className="space-y-3" aria-label="Fichas de cobranza por factura vencida">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-base font-semibold text-ink">Facturas vencidas y su responsable</h2>
          <p className="text-xs text-ink-muted">Cada factura vencida debe tener un responsable y una siguiente revisión.</p>
        </div>
        <label className="flex items-center gap-2 text-xs text-ink-muted">
          <input type="checkbox" checked={soloSin} onChange={(e) => setSoloSin(e.target.checked)} />
          Solo sin responsable
        </label>
      </div>
      {fichas.isError ? (
        <p className="text-sm text-danger">No se pudieron cargar las fichas.</p>
      ) : filas.length === 0 ? (
        <p className="text-sm text-ink-muted">{fichas.isLoading ? 'Cargando…' : 'No hay facturas vencidas con esos filtros.'}</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Cliente</TableHead>
              <TableHead>Factura</TableHead>
              <TableHead className="text-right">Saldo</TableHead>
              <TableHead>Vencida</TableHead>
              <TableHead>Responsable</TableHead>
              <TableHead>Siguiente revisión</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {filas.map((f) => (
              <TableRow key={f.facturaCarteraId}>
                <TableCell>{f.clienteNombre}</TableCell>
                <TableCell>{f.folio}</TableCell>
                <TableCell className="text-right">{importe(f.saldo)} {f.moneda}</TableCell>
                <TableCell>{f.diasVencida} d</TableCell>
                <TableCell>
                  {f.responsableNombre ?? (f.fichaId ? '—' : <Badge variant="outline">Sin responsable</Badge>)}
                  {f.escalada && <Badge className="ml-2" variant="outline">Escalada</Badge>}
                  {f.cerrada && <Badge className="ml-2" variant="outline">Cerrada</Badge>}
                </TableCell>
                <TableCell>
                  {f.siguienteRevision ? formatDate(f.siguienteRevision) : '—'}
                  {f.revisionVencida && <Badge className="ml-2" variant="outline">Revisión vencida</Badge>}
                </TableCell>
                <TableCell className="space-x-1 text-right">
                  <Button size="sm" variant="ghost" onClick={() => setHistorial(historial === f.facturaCarteraId ? null : f.facturaCarteraId)}>
                    Historial
                  </Button>
                  {puedeRegistrar && !f.cerrada && (
                    <>
                      <Button size="sm" variant="outline" onClick={() => setObjetivo({ modo: 'gestion', factura: f })}>Gestión</Button>
                      <Button size="sm" variant="outline" onClick={() => setObjetivo({ modo: 'responsable', factura: f })}>Responsable</Button>
                      {f.fichaId && (
                        <>
                          <Button size="sm" variant="outline" onClick={() => setObjetivo({ modo: 'reprogramar', factura: f })}>Reprogramar</Button>
                          <Button size="sm" variant="outline" onClick={() => setObjetivo({ modo: 'escalar', factura: f })}>Escalar</Button>
                          <Button size="sm" variant="outline" onClick={() => setObjetivo({ modo: 'cerrar', factura: f })}>Cerrar</Button>
                        </>
                      )}
                    </>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {historial && (
        <div className="rounded-md border border-line p-3 text-sm" aria-label="Historial de la ficha">
          {(eventos.data ?? []).length === 0 ? (
            <p className="text-ink-muted">{eventos.isLoading ? 'Cargando…' : 'Sin movimientos todavía.'}</p>
          ) : (
            <ul className="space-y-2">
              {(eventos.data ?? []).map((e) => {
                const fila = filas.find((x) => x.facturaCarteraId === historial);
                return (
                  <li key={e.id}>
                    <span className="font-medium">{e.tipoNombre}</span> · {formatDate(e.fecha)} · {e.usuarioNombre ?? '—'}
                    {e.estadoPromesa && (
                      <Badge className="ml-2" variant="outline">
                        {ETIQUETA_PROMESA[e.estadoPromesa]}
                        {e.estadoPromesa !== 'Vigente' && e.faltantePromesa != null && e.faltantePromesa > 0
                          ? ` · falta ${importe(e.faltantePromesa)}`
                          : ''}
                      </Badge>
                    )}
                    <div className="text-ink-muted">
                      {e.nota}
                      {e.responsableAntes || e.responsableDespues ? ` (${e.responsableAntes ?? '—'} → ${e.responsableDespues ?? '—'})` : ''}
                      {e.revisionAntes || e.revisionDespues ? ` (revisión ${e.revisionAntes ?? '—'} → ${e.revisionDespues ?? '—'})` : ''}
                      {e.notaAnterior ? ` · antes: «${e.notaAnterior}» · motivo: ${e.motivoCorreccion}` : ''}
                    </div>
                    {puedeRegistrar && fila && ['Gestion', 'Promesa', 'Reprogramacion', 'Escalamiento'].includes(e.tipoNombre) && (
                      <Button size="sm" variant="ghost" onClick={() => setObjetivo({ modo: 'corregir', factura: fila, eventoId: e.id, notaActual: e.nota })}>
                        Corregir nota
                      </Button>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}
      {objetivo && (
        <FichaCobranzaDialog
          key={`${objetivo.modo}-${objetivo.eventoId ?? objetivo.factura.facturaCarteraId}`}
          open
          onOpenChange={(o) => !o && setObjetivo(null)}
          modo={objetivo.modo}
          objetivo={`${objetivo.factura.clienteNombre} · ${objetivo.factura.folio}`}
          personas={personas}
          notaActual={objetivo.notaActual}
          isPending={pendiente}
          onConfirm={confirmar}
        />
      )}
    </section>
  );
}
