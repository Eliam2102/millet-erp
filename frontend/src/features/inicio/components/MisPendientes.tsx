import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { useAuthStore } from '@/lib/auth/auth-store';
import { hoyLocalISO } from '@/lib/datetime';
import { useEjercicios } from '@/features/contabilidad/api/periodos';
import { pendientes, pendienteVisible, type PendienteConfig } from '../config';
import { nivelRequisiciones, useConteosInicio } from '../api/useConteosInicio';
import { FilaPendiente } from './FilaPendiente';

function FilaPeriodo({ fila }: { fila: PendienteConfig }) {
  const query = useEjercicios({ retry: 1, staleTime: 60_000 });
  const [anio, mes] = hoyLocalISO().split('-').map(Number);
  const periodo = query.data
    ?.find((e) => e.anio === anio)
    ?.periodos.find((p) => p.numero === mes && !p.esAjuste);
  const etiqueta = !periodo
    ? 'No configurado'
    : periodo.estado === 'NoAbierto'
      ? 'No abierto'
      : periodo.estado;
  const variante =
    periodo?.estado === 'Abierto'
      ? 'success'
      : periodo?.estado === 'Cerrado'
        ? 'neutral'
        : 'warning';
  return (
    <FilaPendiente
      fila={fila}
      query={query}
      etiquetaEstado={etiqueta}
      estado={<Badge variant={variante}>{etiqueta}</Badge>}
    />
  );
}

export function MisPendientes() {
  const permisos = useAuthStore((s) => s.permisos);
  const visibles = pendientes.filter((fila) => pendienteVisible(fila, permisos));
  const ids = visibles.flatMap((fila) => (fila.conteo ? [fila.conteo] : []));
  const queries = useConteosInicio(ids, permisos);
  const nivelRq = nivelRequisiciones(permisos);
  const filas = visibles.map((fila) => ({
    fila:
      fila.conteo === 'requisiciones' && nivelRq !== undefined
        ? { ...fila, search: { nivelPendiente: nivelRq } }
        : fila,
    query: fila.conteo ? queries[ids.indexOf(fila.conteo)] : undefined,
  }));
  const conPendientes = (item: (typeof filas)[number]) =>
    !item.query?.isError && (item.query?.data?.total ?? 0) > 0;
  filas.sort((a, b) => Number(conPendientes(b)) - Number(conPendientes(a)));
  return (
    <section aria-labelledby="mis-pendientes">
      <Card className="rounded-xl bg-surface-card text-ink shadow-card">
        <CardHeader>
          <h2 id="mis-pendientes" className="text-lg font-semibold">
            Mis pendientes
          </h2>
          <p className="text-xs text-ink-muted">
            Requiere tu acción · Registros disponibles según tus permisos.
          </p>
        </CardHeader>
        {filas.length === 0 ? (
          <CardContent>
            <p className="text-sm font-medium">Sin pendientes para tu rol</p>
            <p className="mt-1 text-xs text-ink-muted">
              Aquí aparecerán las bandejas de trabajo disponibles con tus permisos.
            </p>
          </CardContent>
        ) : (
          <ul>
            {filas.map(({ fila, query }) =>
              fila.periodo ? (
                <FilaPeriodo key={fila.id} fila={fila} />
              ) : (
                <FilaPendiente key={fila.id} fila={fila} query={query} conteo={query?.data} />
              ),
            )}
          </ul>
        )}
      </Card>
    </section>
  );
}
