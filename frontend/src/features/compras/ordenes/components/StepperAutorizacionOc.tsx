import { Check, X } from 'lucide-react';
import { cn } from '@/lib/utils';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';

/**
 * <c>&lt;StepperAutorizacionOc/&gt;</c> — stepper visual del flujo de
 * autorización de una OC (FOC9 cerrado del 05 §9.1).
 *
 * <para>Cuatro pasos canónicos: Borrador → Jefe Compras (N1) →
 * Dirección (N2) → Autorizada. El estado actual de la OC determina
 * qué paso está activo, cuáles ya están cumplidos, y si la OC fue
 * rechazada (badge X en el paso donde se cortó). Para OCs canceladas,
 * el stepper muestra el último paso conocido como "actual" con badge
 * de cancelación neutral — no podemos saber dónde se canceló sin el
 * histórico (UF7-PR3 cuando lo wireemos).</para>
 *
 * <para><b>Scope UF1-PR1</b>: deriva los pasos completados del
 * <c>estado</c> únicamente (cabecera). Cuando UF7-PR3 traiga el
 * histórico de autorizaciones, se enriquece con tooltips que muestran
 * "Aprobado por X el Y" en cada paso completado. La firma del
 * componente acepta props opcionales para esa data sin romper el
 * call-site actual.</para>
 *
 * <para><b>Accesibilidad</b>: cada paso combina ícono + label + color +
 * <c>aria-current</c> en el paso activo. Tooltips nativos vía
 * <c>title</c> (no Radix Tooltip — el stepper es siempre visible y no
 * necesita interacción de hover compleja).</para>
 */
export interface StepperAutorizacionOcProps {
  estado: EstadoOrdenCompra;
  /** Override de clases del contenedor exterior. */
  className?: string;
}

type EstadoPaso = 'pendiente' | 'actual' | 'completo' | 'rechazado';

interface PasoInfo {
  /** Identificador estable para tests/scraping. */
  id: 'borrador' | 'n1' | 'n2' | 'autorizada';
  /** Etiqueta humana del paso. */
  label: string;
  /** Estado del paso derivado del <c>EstadoOrdenCompra</c> de la OC. */
  estado: EstadoPaso;
}

const PASOS_BASE: ReadonlyArray<{ id: PasoInfo['id']; label: string }> = [
  { id: 'borrador', label: 'Borrador' },
  { id: 'n1', label: 'Jefe Compras (N1)' },
  { id: 'n2', label: 'Dirección (N2)' },
  { id: 'autorizada', label: 'Autorizada' },
];

/**
 * Mapeo <c>EstadoOrdenCompra</c> → estado de cada uno de los 4 pasos.
 * Centralizado acá para que la UI sea derivable y testeable celda por
 * celda.
 */
function derivarPasos(estado: EstadoOrdenCompra): PasoInfo[] {
  const map = (
    bord: EstadoPaso,
    n1: EstadoPaso,
    n2: EstadoPaso,
    aut: EstadoPaso,
  ): PasoInfo[] =>
    PASOS_BASE.map((p, i) => ({
      ...p,
      estado: [bord, n1, n2, aut][i]!,
    }));

  switch (estado) {
    case EstadoOrdenCompra.Borrador:
      return map('actual', 'pendiente', 'pendiente', 'pendiente');
    case EstadoOrdenCompra.EnAutorizacionJefeCompras:
      return map('completo', 'actual', 'pendiente', 'pendiente');
    case EstadoOrdenCompra.EnAutorizacionDireccion:
      return map('completo', 'completo', 'actual', 'pendiente');
    case EstadoOrdenCompra.Autorizada:
    case EstadoOrdenCompra.Cerrada:
      return map('completo', 'completo', 'completo', 'completo');
    case EstadoOrdenCompra.Rechazada:
      // No sabemos en qué nivel se rechazó sin el histórico — marcamos
      // ambos niveles de autorización como rechazados para señalar el
      // corte. UF7-PR3 refinará usando el último evento.
      return map('completo', 'rechazado', 'rechazado', 'pendiente');
    case EstadoOrdenCompra.Cancelada:
      // Cancelación puede ser pre- o post-autorización; dejamos los
      // pasos como vinieran sin marcarlos rechazados (el badge de
      // estado del header ya comunica "Cancelada").
      return map('completo', 'pendiente', 'pendiente', 'pendiente');
  }
}

const PASO_BG: Record<EstadoPaso, string> = {
  pendiente: 'bg-slate-100 text-slate-500 ring-slate-200',
  actual: 'bg-amber-100 text-amber-900 ring-amber-300',
  completo: 'bg-emerald-100 text-emerald-900 ring-emerald-300',
  rechazado: 'bg-rose-200 text-rose-950 ring-rose-300',
};

const CONECTOR_BG: Record<EstadoPaso, string> = {
  // El conector usa el estado del paso de la IZQUIERDA (origen).
  pendiente: 'bg-slate-200',
  actual: 'bg-amber-300',
  completo: 'bg-emerald-400',
  rechazado: 'bg-rose-300',
};

export function StepperAutorizacionOc({
  estado,
  className,
}: StepperAutorizacionOcProps) {
  const pasos = derivarPasos(estado);

  return (
    <ol
      className={cn(
        'flex w-full items-center gap-1 text-xs',
        className,
      )}
      aria-label="Flujo de autorización de la orden de compra"
      data-component="stepper-autorizacion-oc"
    >
      {pasos.map((paso, i) => {
        const esUltimo = i === pasos.length - 1;
        const conector = !esUltimo ? pasos[i].estado : undefined;
        return (
          <li
            key={paso.id}
            className="flex flex-1 items-center"
            data-paso={paso.id}
            data-estado-paso={paso.estado}
            aria-current={paso.estado === 'actual' ? 'step' : undefined}
          >
            <div
              className={cn(
                'flex min-w-0 flex-1 items-center gap-1.5 rounded-md px-2 py-1 ring-1 ring-inset',
                PASO_BG[paso.estado],
              )}
              title={paso.label}
            >
              <span
                aria-hidden="true"
                className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-current/10"
              >
                {paso.estado === 'completo' && (
                  <Check className="h-3 w-3" />
                )}
                {paso.estado === 'rechazado' && <X className="h-3 w-3" />}
                {paso.estado === 'actual' && (
                  <span className="block h-1.5 w-1.5 rounded-full bg-current" />
                )}
                {paso.estado === 'pendiente' && (
                  <span className="text-[0.65rem] font-semibold leading-none">
                    {i + 1}
                  </span>
                )}
              </span>
              <span className="truncate font-medium">{paso.label}</span>
            </div>
            {conector != null && (
              <span
                aria-hidden="true"
                className={cn(
                  'mx-1 h-0.5 w-3 shrink-0 rounded',
                  CONECTOR_BG[conector],
                )}
              />
            )}
          </li>
        );
      })}
    </ol>
  );
}
