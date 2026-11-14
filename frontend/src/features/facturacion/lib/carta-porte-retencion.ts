import { CONCEPTOS_RETENCION_CLIENTE } from '@/features/facturacion/api/types';

/** Redondeo a centavos, mitades hacia arriba (como el backend con AwayFromZero). */
const centavos = (n: number) => Math.sign(n) * Math.round(Math.abs(n) * 100 + Number.EPSILON) / 100;

/** Tasa de retención de IVA del concepto (FLETES 4 %, SERVICIOS 6 %, …); null sin concepto o si no existe. */
export function tasaRetencionDe(concepto: string | null | undefined): number | null {
  if (!concepto) return null;
  return CONCEPTOS_RETENCION_CLIENTE.find((c) => c.value === concepto)?.tasa ?? null;
}

/**
 * P51: totales del servicio de flete de una Carta Porte tipo I, con la misma aritmética que el backend
 * (`CartaPorte.CrearBorrador`): IVA = monto × tasa, retención = monto × tasa del concepto, total = monto + IVA − retención.
 * El tipo T (traslado) no lleva importes.
 */
export function totalesCartaPorte(
  tipoCfdi: string,
  montoServicio: number,
  tasaIva: number | null,
  conceptoRetencion: string | null | undefined,
) {
  if (tipoCfdi !== 'I' || !(montoServicio > 0)) return { iva: 0, retencion: 0, total: 0 };
  const iva = tasaIva && tasaIva > 0 ? centavos(montoServicio * tasaIva) : 0;
  const tasaRet = tasaRetencionDe(conceptoRetencion);
  const retencion = tasaRet ? centavos(montoServicio * tasaRet) : 0;
  return { iva, retencion, total: centavos(montoServicio + iva - retencion) };
}
