import { HandCoins, Mail, MessageCircle, Phone } from 'lucide-react';
import { DateTimeDisplay } from '@/components/erp';
import {
  CanalCobranza,
  ResultadoCobranza,
  type SeguimientoCobranzaResponse,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_CANAL_COBRANZA,
  ETIQUETA_RESULTADO_COBRANZA,
} from '@/features/cxc/lib/glosario';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TimelineCobranza/&gt;</c> — historial vertical de gestiones de
 * cobranza (CXC-FE-PR4, componente nuevo del 05-frontend-diseno §5).
 * Append-only: se lee de más reciente a más antigua, cada entrada con
 * canal (icono), resultado (chip), nota y — en promesa de pago — el
 * compromiso monto/fecha destacado.
 */
export function TimelineCobranza({
  items,
  nombreUsuario,
  moneda = 'MXN',
}: {
  items: readonly SeguimientoCobranzaResponse[];
  /** Resolución usuarioId → nombre (fallback id corto). */
  nombreUsuario: (id: string) => string;
  /** Solo etiqueta el monto comprometido; el backend no guarda divisa
   * por gestión (el compromiso se pacta en la moneda de la cartera). */
  moneda?: string;
}) {
  return (
    <ol className="relative space-y-6 border-l pl-6" aria-label="Historial de gestiones">
      {items.map((s) => (
        <li key={s.id} className="relative">
          <span
            className="absolute -left-[31px] flex h-6 w-6 items-center justify-center rounded-full border bg-background"
            aria-hidden="true"
          >
            <IconoCanal canal={s.canal} />
          </span>
          <div className="flex flex-wrap items-center gap-2">
            <ChipResultadoCobranza resultado={s.resultado} />
            <span className="text-xs text-muted-foreground">
              {ETIQUETA_CANAL_COBRANZA[s.canal]} · {nombreUsuario(s.usuarioId)} ·{' '}
              <DateTimeDisplay value={s.fecha} />
            </span>
          </div>
          {s.resultado === ResultadoCobranza.PromesaPago &&
            s.montoComprometido != null && (
              <p className="mt-1 flex items-center gap-1.5 text-sm font-medium">
                <HandCoins className="h-4 w-4 text-emerald-600" aria-hidden="true" />
                Compromiso:{' '}
                <span className="font-mono tabular-nums">
                  {s.montoComprometido.toLocaleString('es-MX', {
                    minimumFractionDigits: 2,
                  })}{' '}
                  {moneda}
                </span>
                {s.fechaComprometida && <> para el {s.fechaComprometida}</>}
              </p>
            )}
          <p className="mt-1 whitespace-pre-wrap text-sm text-muted-foreground">
            {s.nota}
          </p>
        </li>
      ))}
    </ol>
  );
}

function IconoCanal({ canal }: { canal: CanalCobranza }) {
  const clases = 'h-3.5 w-3.5 text-muted-foreground';
  switch (canal) {
    case CanalCobranza.Llamada:
      return <Phone className={clases} aria-hidden="true" />;
    case CanalCobranza.Correo:
      return <Mail className={clases} aria-hidden="true" />;
    case CanalCobranza.Whatsapp:
      return <MessageCircle className={clases} aria-hidden="true" />;
  }
}

export function ChipResultadoCobranza({
  resultado,
}: {
  resultado: ResultadoCobranza;
}) {
  const clases: Record<ResultadoCobranza, string> = {
    [ResultadoCobranza.PromesaPago]:
      'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300',
    [ResultadoCobranza.SinRespuesta]: 'bg-muted text-muted-foreground',
    [ResultadoCobranza.Excusa]:
      'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
    [ResultadoCobranza.Otro]: 'bg-muted text-muted-foreground',
  };
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
        clases[resultado],
      )}
    >
      {ETIQUETA_RESULTADO_COBRANZA[resultado]}
    </span>
  );
}
