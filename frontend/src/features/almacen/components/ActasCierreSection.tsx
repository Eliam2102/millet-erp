import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { esApiError } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useActasCierre, useRevisarActaCierre, type ActaCierre } from '@/features/almacen/api/useAvisosAlmacen';

const MXN = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });
const fecha = (iso: string) => new Date(iso).toLocaleString('es-MX');

function ActaCard({ acta, puedeRevisar }: { acta: ActaCierre; puedeRevisar: boolean }) {
  const revisar = useRevisarActaCierre();
  const periodo = `${acta.anio}-${String(acta.mes).padStart(2, '0')}`;
  const { almacenes, conteos, vales } = acta.contenido;
  return (
    <article aria-label={`Acta ${periodo}`} data-estado={acta.estado} className="space-y-3 rounded-lg border border-line-row bg-surface-card p-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold text-ink-strong">Acta del cierre {periodo}</h3>
          <p className="text-xs text-ink-muted">Ejecutó {acta.ejecutadoPor} el {fecha(acta.ejecutadoAt)}</p>
        </div>
        {acta.estado === 'REVISADA'
          ? <Badge variant="success">Revisada</Badge>
          : <Badge variant="warning">Por revisar</Badge>}
      </header>
      <table className="w-full text-sm">
        <thead className="text-left text-xs text-ink-muted">
          <tr><th className="py-1 font-medium">Almacén</th><th className="py-1 text-right font-medium">Renglones</th><th className="py-1 text-right font-medium">Valor del inventario</th></tr>
        </thead>
        <tbody>
          {almacenes.map((a) => (
            <tr key={a.almacen} className="border-t border-line-row">
              <td className="py-1">{a.almacen}</td>
              <td className="py-1 text-right tabular-nums">{a.renglones}</td>
              <td className="py-1 text-right tabular-nums">{MXN.format(a.valorInventarioMxn)}</td>
            </tr>
          ))}
          {almacenes.length === 0 && <tr><td colSpan={3} className="py-1 text-xs text-ink-muted">Sin existencias valuadas al cierre.</td></tr>}
        </tbody>
      </table>
      <p className="text-xs text-ink-muted">
        Conteos aplicados: {conteos.length ? conteos.map((c) => `${c.folio} (${c.tipo})`).join(', ') : 'ninguno'}.
        {' '}Vales atendidos: {vales.length ? vales.map((v) => v.folio).join(', ') : 'ninguno'}.
      </p>
      {acta.estado === 'REVISADA'
        ? <p className="text-xs text-ink-muted">Revisada por {acta.revisadaPor} el {acta.revisadaAt && fecha(acta.revisadaAt)}</p>
        : puedeRevisar
          ? <Button size="sm" disabled={revisar.isPending}
            onClick={() => revisar.mutate(acta.id, {
              onSuccess: () => toast.success('Acta marcada como revisada', { description: `Periodo ${periodo}.` }),
              onError: (e) => toast.error(esApiError(e) ? e.problem.detail ?? e.problem.title : 'No se pudo marcar el acta.'),
            })}>Marcar revisada</Button>
          : <p className="text-xs text-ink-muted">La revisa la Dirección de Administración.</p>}
    </article>
  );
}

/** Actas del cierre mensual (86e3tac2y): la Dirección de Administración las marca revisadas con `almacen.cierre.revisar-acta`. */
export function ActasCierreSection() {
  const puedeRevisar = useAuthStore((s) => s.permisos.includes(PermisosCanonicos.AlmacenCierreRevisarActa));
  const query = useActasCierre();
  return (
    <section aria-labelledby="actas-cierre-titulo" className="space-y-3">
      <h2 id="actas-cierre-titulo" className="text-sm font-semibold text-ink-strong">Actas del cierre</h2>
      {query.isError && <AvisoEnLinea tono="error" titulo="No se pudieron cargar las actas">Recarga la página.</AvisoEnLinea>}
      {query.data?.length === 0 && <p className="text-xs text-ink-muted">Todavía no hay cierres con acta.</p>}
      {query.data?.map((a) => <ActaCard key={a.id} acta={a} puedeRevisar={puedeRevisar} />)}
    </section>
  );
}
