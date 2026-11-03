/**
 * Cuentas del complemento de pago (CtaOrdenante / CtaBeneficiario) según el
 * catálogo c_FormaPago del SAT (86e3tfdve). Espejo de
 * `Facturacion.Domain.Repp.CuentasPagoSat` del backend: si cambia una regla,
 * cambia en ambos lados. El backend es quien decide (REPP_CUENTA_INVALIDA);
 * aquí solo se adelanta el error en el formulario.
 */

export type TipoCuentaPago = 'ordenante' | 'beneficiaria';

interface FormatoCuenta {
  patron: RegExp;
  descripcion: string;
}

interface ReglaCuentas {
  nombre: string;
  ordenante: FormatoCuenta | null;
  beneficiaria: FormatoCuenta | null;
}

const GENERICO: FormatoCuenta = {
  patron: /^(?:[0-9]{10,11}|[0-9]{15,16}|[0-9]{18}|[A-Z0-9_]{10,50})$/,
  descripcion: '10, 11, 15, 16 o 18 dígitos, o de 10 a 50 caracteres alfanuméricos',
};

/** Formas bancarizadas; cualquier otra clave no admite cuentas. */
export const REGLAS_CUENTAS_PAGO: Readonly<Record<string, ReglaCuentas>> = {
  '02': {
    nombre: 'Cheque nominativo',
    ordenante: { patron: /^(?:[0-9]{11}|[0-9]{18})$/, descripcion: '11 o 18 dígitos' },
    beneficiaria: GENERICO,
  },
  '03': {
    nombre: 'Transferencia electrónica de fondos',
    ordenante: { patron: /^(?:[0-9]{10}|[0-9]{16}|[0-9]{18})$/, descripcion: '10, 16 o 18 dígitos' },
    beneficiaria: { patron: /^(?:[0-9]{10}|[0-9]{18})$/, descripcion: '10 o 18 dígitos' },
  },
  '04': {
    nombre: 'Tarjeta de crédito',
    ordenante: { patron: /^[0-9]{16}$/, descripcion: '16 dígitos' },
    beneficiaria: GENERICO,
  },
  '05': { nombre: 'Monedero electrónico', ordenante: GENERICO, beneficiaria: GENERICO },
  '06': {
    nombre: 'Dinero electrónico',
    ordenante: { patron: /^[0-9]{10}$/, descripcion: '10 dígitos' },
    beneficiaria: null,
  },
  '28': {
    nombre: 'Tarjeta de débito',
    ordenante: { patron: /^[0-9]{16}$/, descripcion: '16 dígitos' },
    beneficiaria: GENERICO,
  },
  '29': {
    nombre: 'Tarjeta de servicios',
    ordenante: { patron: /^[0-9]{15,16}$/, descripcion: '15 o 16 dígitos' },
    beneficiaria: GENERICO,
  },
};

function regla(formaPago: string | null | undefined): ReglaCuentas | null {
  return REGLAS_CUENTAS_PAGO[(formaPago ?? '').trim()] ?? null;
}

export function admiteOrdenante(formaPago: string | null | undefined): boolean {
  return regla(formaPago)?.ordenante != null;
}

export function admiteBeneficiaria(formaPago: string | null | undefined): boolean {
  return regla(formaPago)?.beneficiaria != null;
}

/** Quita espacios y guiones de captura; vacío = sin cuenta. */
export function normalizarCuenta(cuenta: string | null | undefined): string | null {
  const limpia = (cuenta ?? '').replace(/[\s-]/g, '').toUpperCase();
  return limpia.length > 0 ? limpia : null;
}

/** «•••• 1234»; respeta un valor que ya viene enmascarado del servidor. */
export function enmascararCuenta(cuenta: string | null | undefined): string | null {
  const v = (cuenta ?? '').trim();
  if (!v) return null;
  if (v.includes('•')) return v;
  return v.length <= 4 ? '••••' : `•••• ${v.slice(-4)}`;
}

/** Texto de por qué la forma no admite la cuenta (para hint de campo deshabilitado). */
export function motivoSinCuenta(formaPago: string, tipo: TipoCuentaPago): string {
  const r = regla(formaPago);
  return `La forma de pago ${formaPago.trim()}${r ? ` (${r.nombre})` : ''} no admite cuenta ${tipo}.`;
}

/** Error legible (mismo texto que el backend) o null si la cuenta es válida o está vacía. */
export function errorCuentaPago(
  formaPago: string,
  tipo: TipoCuentaPago,
  cuenta: string | null | undefined,
): string | null {
  const valor = normalizarCuenta(cuenta);
  if (valor == null) return null;
  const r = regla(formaPago);
  const formato = r?.[tipo] ?? null;
  if (formato == null)
    return `${motivoSinCuenta(formaPago, tipo).slice(0, -1)}; quita la cuenta ${enmascararCuenta(valor)} o cambia la forma de pago.`;
  if (!formato.patron.test(valor))
    return `La cuenta ${tipo} ${enmascararCuenta(valor)} no tiene el formato del SAT para la forma de pago ${formaPago.trim()} (${r!.nombre}): se esperan ${formato.descripcion}.`;
  return null;
}
