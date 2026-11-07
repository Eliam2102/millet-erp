import { Input } from '@/components/ui/input';
import { useTipoCambioPago } from '@/features/tesoreria/api/useTesoreria';

export interface TipoCambioPagoCampoProps {
  id: string;
  /** Moneda del pago o del cobro; en MXN (o sin moneda) el campo no se muestra. */
  moneda: string | null;
  /** Fecha valor (YYYY-MM-DD): el TC del catálogo se busca a esa fecha. */
  fecha: string;
  /** Lo que capturó el usuario; null = sin cambios (se muestra el del catálogo y el backend usa ese mismo). */
  value: string | null;
  onChange: (valor: string) => void;
  etiqueta?: string;
}

/** TES-09: valor a mandar al backend; undefined = MXN, sin cambios o vacío (el backend usa el del catálogo a la fecha valor). */
export function tipoCambioPagoParaEnviar(moneda: string | null, valor: string | null): number | undefined {
  if (moneda == null || moneda === 'MXN' || valor == null || valor.trim() === '') return undefined;
  return Number(valor);
}

/** TES-09: null si es válido o no aplica; si no, el motivo para deshabilitar el envío. */
export function errorTipoCambioPago(moneda: string | null, valor: string | null): string | null {
  if (moneda == null || moneda === 'MXN' || valor == null || valor.trim() === '') return null;
  const n = Number(valor);
  if (!Number.isFinite(n) || n <= 0) return 'El TC del pago debe ser mayor a cero';
  if (!/^\d+(\.\d{1,6})?$/.test(valor.trim())) return 'El TC del pago admite hasta 6 decimales';
  return null;
}

/**
 * TES-09 (86e3jvvj4) · «TC del pago»: solo en moneda distinta de MXN. Muestra el TC del catálogo a la fecha valor mientras
 * el usuario no lo cambie (sin efecto: el valor mostrado se deriva). Solo se manda si lo cambió; si no, el backend toma ese
 * mismo TC del catálogo.
 */
export function TipoCambioPagoCampo({
  id,
  moneda,
  fecha,
  value,
  onChange,
  etiqueta = 'TC del pago',
}: TipoCambioPagoCampoProps) {
  const aplica = moneda != null && moneda !== 'MXN';
  const catalogo = useTipoCambioPago(aplica ? moneda : null, fecha);
  const valorCatalogo = catalogo.data?.valorEnMxn ?? null;

  if (!aplica) return null;

  const mostrado = value ?? (valorCatalogo == null ? '' : String(valorCatalogo));

  const error = errorTipoCambioPago(moneda, value);
  const ayuda = catalogo.isFetching
    ? 'Buscando el TC del catálogo…'
    : catalogo.isError
      ? 'No se pudo consultar el catálogo; si lo dejas vacío, se usa el TC del catálogo de esa fecha.'
      : valorCatalogo == null
        ? `El catálogo no tiene TC de ${moneda} para esa fecha; captura el que aplicó el banco.`
        : `Catálogo: ${valorCatalogo} MXN por ${moneda}. Cámbialo si el banco aplicó otro.`;

  return (
    <div className="space-y-1">
      <label className="text-xs text-ink-muted" htmlFor={id}>
        {etiqueta} (MXN por {moneda})
      </label>
      <Input
        id={id}
        type="number"
        inputMode="decimal"
        min="0"
        step="0.000001"
        value={mostrado}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={error != null}
        className="text-right font-mono"
      />
      <p className={error ? 'text-xs text-danger-fg' : 'text-xs text-ink-muted'}>{error ?? ayuda}</p>
    </div>
  );
}
