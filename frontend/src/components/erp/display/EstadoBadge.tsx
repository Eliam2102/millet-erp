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
 * <para>Cada estado tiene un color codificado en clases Tailwind
 * para mantener tonos consistentes con el resto del shell. La paleta
 * sigue la convención del doc: gris para borrador / ámbar para en
 * autorización / azul para autorizada / morado para en surtido / verde
 * para cerrada / rojo claro para canceladas / rojo intenso para
 * rechazadas.</para>
 *
 * <para><b>Contraste WCAG AA</b>: cada par fondo/texto se verificó a
 * mano (≥ 4.5:1) antes de mergear. Los 7 colores nuevos de OC heredan
 * los pares ya verificados de RQ para los estados compartidos
 * (<c>Borrador</c>, <c>Autorizada</c>, <c>Cancelada</c>,
 * <c>Rechazada</c>) e introducen 3 pares nuevos
 * (<c>EnAutorizacionJefeCompras</c>, <c>EnAutorizacionDireccion</c> en
 * la familia ámbar/azul oscuro y <c>Cerrada</c> en verde) verificados
 * de la misma forma. Tabla en el body del PR.</para>
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

const COLORES_RQ: Record<EstadoRequisicion, string> = {
  // Borrador: gris neutral.
  [EstadoRequisicion.Borrador]: 'bg-slate-100 text-slate-700 ring-slate-200',
  // EnAutorizacion: ámbar (en proceso, requiere atención).
  [EstadoRequisicion.EnAutorizacion]:
    'bg-amber-100 text-amber-900 ring-amber-200',
  // Autorizada: azul (validada, lista para ejecución).
  [EstadoRequisicion.Autorizada]: 'bg-blue-100 text-blue-900 ring-blue-200',
  // EnSurtido: morado (workflow operativo en marcha).
  [EstadoRequisicion.EnSurtido]:
    'bg-violet-100 text-violet-900 ring-violet-200',
  // Cerrada: verde (cumplida, exitosa).
  [EstadoRequisicion.Cerrada]:
    'bg-emerald-100 text-emerald-900 ring-emerald-200',
  // Cancelada: rojo claro (terminado sin cumplirse).
  [EstadoRequisicion.Cancelada]: 'bg-rose-100 text-rose-900 ring-rose-200',
  // Rechazada: rojo más intenso (decisión negativa).
  [EstadoRequisicion.Rechazada]: 'bg-rose-200 text-rose-950 ring-rose-300',
  // Eliminada: gris oscuro (descartada pre-autorización).
  [EstadoRequisicion.Eliminada]: 'bg-stone-200 text-stone-700 ring-stone-300',
  // CerradaSinSurtir: zinc (cierre administrativo sin entrega — ADR-0043).
  // Par fondo/texto AA verificado (zinc-100/zinc-800 ≥ 4.5:1).
  [EstadoRequisicion.CerradaSinSurtir]:
    'bg-zinc-100 text-zinc-800 ring-zinc-300',
  // CerradaSurtidaParcial: teal apagado (cierre con entrega parcial —
  // ADR-0043). Par teal-100/teal-900 AA verificado.
  [EstadoRequisicion.CerradaSurtidaParcial]:
    'bg-teal-100 text-teal-900 ring-teal-200',
};

// Paleta de la situación de surtido (ADR-0043). Tres tonos distintos del
// violeta genérico de EnSurtido para que las tres fases se distingan de un
// vistazo dentro de bandeja. Pares fondo/texto AA verificados (≥ 4.5:1).
const COLORES_SITUACION: Record<SituacionSurtido, string> = {
  // EsperandoCompra: violeta (todo por comprar; conserva el tono histórico
  // de "en surtido" para el caso de arranque).
  [SituacionSurtido.EsperandoCompra]:
    'bg-violet-100 text-violet-900 ring-violet-200',
  // ListoParaSurtir: azul cielo (material disponible, acción inminente).
  [SituacionSurtido.ListoParaSurtir]: 'bg-sky-100 text-sky-900 ring-sky-200',
  // SurtidoParcial: fucsia (avance parcial, en curso).
  [SituacionSurtido.SurtidoParcial]:
    'bg-fuchsia-100 text-fuchsia-900 ring-fuchsia-200',
};

const COLORES_OC: Record<EstadoOrdenCompra, string> = {
  // Borrador: gris neutral (mismo par que RQ).
  [EstadoOrdenCompra.Borrador]: 'bg-slate-100 text-slate-700 ring-slate-200',
  // EnAutorizacionJefeCompras: ámbar claro (N1, primera espera).
  [EstadoOrdenCompra.EnAutorizacionJefeCompras]:
    'bg-amber-100 text-amber-900 ring-amber-200',
  // EnAutorizacionDireccion: ámbar más intenso (N2, ya pasó N1, pendiente Dirección).
  [EstadoOrdenCompra.EnAutorizacionDireccion]:
    'bg-orange-100 text-orange-900 ring-orange-200',
  // Autorizada: azul (mismo par que RQ — N1+N2 completas, transmitida).
  [EstadoOrdenCompra.Autorizada]: 'bg-blue-100 text-blue-900 ring-blue-200',
  // Cerrada: verde (mismo par que RQ — todo recibido/facturado/pagado).
  [EstadoOrdenCompra.Cerrada]:
    'bg-emerald-100 text-emerald-900 ring-emerald-200',
  // Cancelada: rojo claro (mismo par que RQ).
  [EstadoOrdenCompra.Cancelada]: 'bg-rose-100 text-rose-900 ring-rose-200',
  // Rechazada: rojo intenso (mismo par que RQ).
  [EstadoOrdenCompra.Rechazada]: 'bg-rose-200 text-rose-950 ring-rose-300',
};

export function EstadoBadge(props: EstadoBadgeProps) {
  if (props.tipo === 'orden-compra') {
    const label = estadoOcToString(props.estado);
    const key = estadoOcToKey(props.estado);
    const definicion = obtenerDefinicionOc(key)?.resumen;
    return (
      <DomainTermTooltip term={key} definicion={definicion}>
        <span
          className={cn(
            'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset',
            COLORES_OC[props.estado],
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
  const colorClass =
    situacion != null
      ? COLORES_SITUACION[situacion]
      : COLORES_RQ[props.estado];
  const definicion = obtenerDefinicion(glosarioKey)?.resumen;

  return (
    <DomainTermTooltip term={glosarioKey} definicion={definicion}>
      <span
        className={cn(
          'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset',
          colorClass,
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
