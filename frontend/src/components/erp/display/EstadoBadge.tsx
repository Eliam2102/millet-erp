import { badgeVariants, type BadgeProps } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { DomainTermTooltip } from '@/components/erp/feedback/DomainTermTooltip';
import {
  EstadoRequisicion,
  SituacionSurtido,
  estadoToKey,
  estadoToString,
  situacionToKey,
  situacionToString,
} from '@/features/compras/api/types';
import {
  EstadoOrdenCompra,
  estadoOcToKey,
  estadoOcToString,
} from '@/features/compras/ordenes/api/types';
import {
  obtenerDefinicion,
  obtenerDefinicionOc,
} from '@/features/compras/lib/glosario';

/**
 * <c>&lt;EstadoBadge/&gt;</c> — badge coloreado para mostrar el estado
 * de un agregado del módulo Compras. Doc 05 §11.3 + §13.7 (RQ) y
 * doc 05 §9.2 (OC) — extensión UF0-PR1.
 *
 * <para>El badge es <b>discriminated union</b> por <c>tipo</c>:
 * <c>'requisicion'</c> para los 10 estados de RQ (8 base + 2 terminales de
 * cierre manual de ADR-0043) (<see cref="EstadoRequisicion"/>) y
 * <c>'orden-compra'</c> para los 7
 * estados de OC (<see cref="EstadoOrdenCompra"/>). Cada tipo tiene su
 * paleta y su lookup de glosario para los tooltips.</para>
 *
 * <para>Colores semánticos del design system; los labels y glosarios
 * siguen los estados reales del dominio.</para>
 *
 * <para><b>Situación de surtido (ADR-0043)</b>: cuando la RQ está
 * <c>EnSurtido</c> y el backend pobló <c>situacion</c>, el badge reemplaza
 * el label genérico "En surtido" por la fase calculada
 * (<see cref="SituacionSurtido"/>: Esperando compra / Listo para surtir /
 * Surtido parcial) con su propio color y tooltip de glosario. La prop es
 * opcional: sin ella (o con <c>null</c>) el badge cae al label por estado,
 * de modo que los callers existentes no cambian. <c>data-estado</c> sigue
 * siendo SIEMPRE la clave de estado (no rompe selectores
 * <c>[data-estado="EnSurtido"]</c>); la situación se expone aparte en
 * <c>data-situacion</c>.</para>
 */
export type EstadoBadgeProps =
  | {
      tipo: 'requisicion';
      estado: EstadoRequisicion;
      /**
       * Situación calculada dentro de <c>EnSurtido</c> (ADR-0043). Solo
       * surte efecto si <c>estado === EnSurtido</c>; en cualquier otro
       * estado se ignora. <c>null</c>/ausente ⇒ label genérico por estado.
       */
      situacion?: SituacionSurtido | null;
      className?: string;
    }
  | {
      tipo: 'orden-compra';
      estado: EstadoOrdenCompra;
      className?: string;
    };

type EstadoVariant = NonNullable<BadgeProps['variant']>;

const VARIANTES_RQ: Record<EstadoRequisicion, EstadoVariant> = {
  [EstadoRequisicion.Borrador]: 'neutral',
  [EstadoRequisicion.EnAutorizacion]: 'warning',
  [EstadoRequisicion.Autorizada]: 'success',
  [EstadoRequisicion.EnSurtido]: 'info',
  [EstadoRequisicion.Cerrada]: 'success',
  [EstadoRequisicion.Cancelada]: 'neutral',
  [EstadoRequisicion.Rechazada]: 'danger',
  [EstadoRequisicion.Eliminada]: 'neutral',
  [EstadoRequisicion.CerradaSinSurtir]: 'neutral',
  [EstadoRequisicion.CerradaSurtidaParcial]: 'info',
};

const VARIANTES_SITUACION: Record<SituacionSurtido, EstadoVariant> = {
  [SituacionSurtido.EsperandoCompra]: 'warning',
  [SituacionSurtido.ListoParaSurtir]: 'info',
  [SituacionSurtido.SurtidoParcial]: 'info',
};

const VARIANTES_OC: Record<EstadoOrdenCompra, EstadoVariant> = {
  [EstadoOrdenCompra.Borrador]: 'neutral',
  [EstadoOrdenCompra.EnAutorizacionJefeCompras]: 'warning',
  [EstadoOrdenCompra.EnAutorizacionDireccion]: 'warning',
  [EstadoOrdenCompra.Autorizada]: 'info',
  [EstadoOrdenCompra.Cerrada]: 'success',
  [EstadoOrdenCompra.Cancelada]: 'neutral',
  [EstadoOrdenCompra.Rechazada]: 'danger',
};

export function EstadoBadge(props: EstadoBadgeProps) {
  if (props.tipo === 'orden-compra') {
    const label = estadoOcToString(props.estado);
    const key = estadoOcToKey(props.estado);
    const definicion = obtenerDefinicionOc(key)?.resumen;
    return (
      <DomainTermTooltip term={key} definicion={definicion}>
        <span
          data-slot="badge"
          className={cn(
            badgeVariants({ variant: VARIANTES_OC[props.estado] }),
            props.className,
          )}
          data-estado={key}
          data-tipo="orden-compra"
        >
          {label}
        </span>
      </DomainTermTooltip>
    );
  }

  // Dentro de EnSurtido, si el backend pobló la situación (ADR-0043), el
  // badge muestra la fase calculada en lugar del label genérico. En
  // cualquier otro estado —o sin situación— cae al camino por estado.
  const estadoKey = estadoToKey(props.estado);
  const situacion =
    props.estado === EstadoRequisicion.EnSurtido && props.situacion != null
      ? props.situacion
      : null;

  const label =
    situacion != null
      ? situacionToString(situacion)
      : estadoToString(props.estado);
  // Clave de glosario para el tooltip: la de la situación cuando aplica,
  // la del estado en caso contrario.
  const glosarioKey = situacion != null ? situacionToKey(situacion) : estadoKey;
  const variant =
    situacion != null
      ? VARIANTES_SITUACION[situacion]
      : VARIANTES_RQ[props.estado];
  const definicion = obtenerDefinicion(glosarioKey)?.resumen;

  return (
    <DomainTermTooltip term={glosarioKey} definicion={definicion}>
      <span
        data-slot="badge"
        className={cn(
          badgeVariants({ variant }),
          props.className,
        )}
        // data-estado SIEMPRE la clave de estado: no rompe selectores
        // [data-estado="EnSurtido"] existentes. La situación va aparte.
        data-estado={estadoKey}
        data-tipo="requisicion"
        data-situacion={situacion != null ? situacionToKey(situacion) : undefined}
      >
        {label}
      </span>
    </DomainTermTooltip>
  );
}
