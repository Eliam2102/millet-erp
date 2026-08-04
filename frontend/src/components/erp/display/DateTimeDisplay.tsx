import { formatDate, formatDateLong, formatDateTime } from '@/lib/datetime';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;DateTimeDisplay/&gt;</c> — formatea un instante UTC del
 * backend a la zona horaria local del ERP (<c>America/Mexico_City</c>,
 * ADR-0013). Doc 05 §11.3.
 *
 * <para>Tres variantes de formato según contexto:</para>
 * <list>
 *   <item><c>'date'</c> (default): <c>02/05/2026</c> — para fecha de
 *   solicitud, fecha de entrega.</item>
 *   <item><c>'datetime'</c>: <c>02/05/2026 14:30</c> — para timestamps
 *   de auditoría, autorizaciones.</item>
 *   <item><c>'long'</c>: <c>2 de mayo de 2026</c> — para impresión PDF
 *   y tarjetas formales.</item>
 * </list>
 *
 * <para>Acepta también valores ya en formato <c>YYYY-MM-DD</c>
 * (<c>DateOnly</c> del backend, sin hora ni TZ). En ese caso se
 * muestra como fecha local sin convertir TZ.</para>
 *
 * <para>Si el valor es <c>null</c>/<c>undefined</c>, renderiza un
 * guion <c>—</c>.</para>
 */
export interface DateTimeDisplayProps {
  /**
   * ISO 8601 UTC (<c>2026-05-02T14:30:00Z</c>) o fecha sin hora
   * (<c>2026-05-02</c>). <c>null</c>/<c>undefined</c> renderiza el
   * placeholder.
   */
  value: string | null | undefined;
  /** <c>'date'</c> | <c>'datetime'</c> | <c>'long'</c>. Default <c>'date'</c>. */
  variant?: 'date' | 'datetime' | 'long';
  className?: string;
  /** Override del placeholder. */
  empty?: string;
}

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

export function DateTimeDisplay({
  value,
  variant = 'date',
  className,
  empty = '—',
}: DateTimeDisplayProps) {
  if (value == null || value === '') {
    return (
      <span
        className={cn('text-muted-foreground', className)}
        aria-label="Sin fecha"
      >
        {empty}
      </span>
    );
  }

  // DateOnly del backend (YYYY-MM-DD) — interpretarlo como fecha local
  // sin shift de TZ. Si fuera ISO completo, lo convertimos a local.
  const esDateOnly = DATE_ONLY.test(value);
  const formatted = formatear(value, variant, esDateOnly);

  return (
    <time className={cn('tabular-nums', className)} dateTime={value}>
      {formatted}
    </time>
  );
}

function formatear(
  value: string,
  variant: 'date' | 'datetime' | 'long',
  esDateOnly: boolean,
): string {
  // Para DateOnly (YYYY-MM-DD), formateamos manualmente sin pasar por
  // TZ conversion: el clásico bug "se mostró un día anterior" ocurre
  // si dejamos que parseISO+toZonedTime conviertan medianoche UTC a
  // la TZ negativa de México (UTC-6) → cae al día anterior.
  if (esDateOnly) {
    const [yyyy, mm, dd] = value.split('-');
    if (variant === 'long') {
      // Construimos un Date local explícito (medianoche local del
      // runtime; formatDateLong lo formatea con date-fns aplicando el
      // shift hacia México, que para fechas sin hora es no-op si el
      // runtime ya está en TZ similar; en CI Node está en UTC y el
      // shift cae al día anterior. Mejor formateo manual también para
      // long).
      return formatLargo(parseInt(yyyy, 10), parseInt(mm, 10), parseInt(dd, 10));
    }
    if (variant === 'datetime') {
      // DateOnly no tiene hora; mostramos solo la fecha aunque pidan datetime.
      return `${dd}/${mm}/${yyyy}`;
    }
    return `${dd}/${mm}/${yyyy}`;
  }

  if (variant === 'long') return formatDateLong(value);
  if (variant === 'datetime') return formatDateTime(value);
  return formatDate(value);
}

const MESES_ES = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
];

function formatLargo(year: number, month: number, day: number): string {
  return `${day} de ${MESES_ES[month - 1]} de ${year}`;
}
