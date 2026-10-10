import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { useMatrizEfectiva } from '../../api/dimensiones';
import { ETIQUETA_DIMENSION, ETIQUETA_REQUERIMIENTO, VARIANTE_REQUERIMIENTO, fechaCorta, origenRequerimiento } from '../../lib/dimensiones';

interface Props {
  cuentaId: string;
  tipoDocumentoId: string;
  fecha: string;
}

/** Qué dimensiones pide una cuenta en un tipo de documento a una fecha (lectura de la configuración vigente). */
export function MatrizEfectiva({ cuentaId, tipoDocumentoId, fecha }: Props) {
  const matriz = useMatrizEfectiva(cuentaId, tipoDocumentoId, fecha);
  if (!cuentaId) return null;
  return (
    <section aria-label="Dimensiones que pide la cuenta" className="rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-sm font-semibold">Dimensiones que pide la cuenta</h2>
      <p className="text-xs text-ink-muted">
        {tipoDocumentoId ? 'Para el tipo de documento elegido' : 'Reglas para todos los tipos de documento'} al {fechaCorta(fecha)}.
      </p>
      {matriz.isLoading ? (
        <Skeleton className="mt-3 h-16 w-full" />
      ) : matriz.isError ? (
        <p role="alert" className="mt-3 text-sm text-danger-fg">No se pudo consultar la configuración de la cuenta.</p>
      ) : (
        <dl className="mt-3 grid gap-3 sm:grid-cols-4">
          {matriz.data!.map((r) => (
            <div key={r.dimension} className="flex flex-col gap-1">
              <dt className="text-xs text-ink-muted">{ETIQUETA_DIMENSION[r.dimension]}</dt>
              <dd className="flex flex-col items-start gap-1">
                <Badge variant={VARIANTE_REQUERIMIENTO[r.requerimiento]}>{ETIQUETA_REQUERIMIENTO[r.requerimiento]}</Badge>
                <span className="text-xs text-ink-muted">{origenRequerimiento(r)}</span>
              </dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
}
