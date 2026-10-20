import { Link } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuthStore } from '@/lib/auth/auth-store';
import { rutaPermitida } from '@/lib/nav';
import { formatoMontoInicio, type PiezaResuelta } from '../api/usePiezasInicio';

export function IndicadoresInicio({ items }: { items: PiezaResuelta[] }) {
  const permisos = useAuthStore(s => s.permisos);
  if (!items.length) return null;
  return <section aria-label="Indicadores" className="grid min-w-0 grid-cols-2 gap-3 lg:flex">
    {items.map(({ pieza, data, estado }) => {
      const contenido = <>
        <span className="text-xs text-ink-muted">{pieza.titulo}</span>
        {estado === 'cargando' ? <Skeleton className="h-7 w-16" /> : estado === 'error' ?
          <p className="text-xs text-danger-fg">No se pudieron cargar los datos.</p> : <>
            <span className="text-3xl font-semibold tabular-nums">{data?.valorTexto ?? (pieza.moneda ? formatoMontoInicio(data?.valor ?? 0) : data?.valor)}</span>
            <span className="text-xs text-ink-muted">{data?.contexto}</span>
            <Badge variant={data?.urgentes ? 'warning' : 'neutral'} className="self-start text-2xs">{data?.urgentes ? `${data.urgentes} requieren atención` : 'Al día'}</Badge>
          </>}
      </>;
      const to = rutaPermitida(pieza.to, permisos) ? pieza.to : pieza.id === 'recepciones' ? '/compras/ordenes' : undefined;
      const estilo = 'flex h-full min-w-0 flex-col gap-1.5 rounded-lg px-4 py-3';
      return <Card key={pieza.id} data-pieza={pieza.id} className="min-w-0 flex-1 bg-surface-card shadow-card">
        {to ? <Link to={to} search={{}} aria-busy={estado === 'cargando' || undefined} className={`${estilo} hover:bg-surface-subtle`}>{contenido}</Link> : <div className={estilo}>{contenido}</div>}
      </Card>;
    })}
  </section>;
}
