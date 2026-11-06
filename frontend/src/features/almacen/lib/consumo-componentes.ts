/** Mismo criterio que `ConsumoComponentes.Calcular`: la unidad M³ (argón) pide el factor m² → m³. */
export const esM3 = (unidad: string | null | undefined) =>
  (unidad ?? '').toUpperCase().replace('³', '3').replace(/[^A-Z0-9]/g, '') === 'M3';
