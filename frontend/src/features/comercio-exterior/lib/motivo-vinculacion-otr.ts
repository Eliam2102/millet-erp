/** Traduce el bloqueo de identidad sin cambiar la validación del servidor. */
export function motivoVinculacionOtr(motivo: string | null): string | null {
  if (!motivo) return null;
  if (motivo === 'CE_OTR_SUCURSAL_DESCONOCIDA')
    return 'Sucursal sin correspondencia en el ERP. Revisa la equivalencia con A+W antes de vincular.';
  if (motivo === 'CE_OTR_CLIENTE_DESCONOCIDO')
    return 'Cliente sin correspondencia en el ERP. Revisa la equivalencia con A+W antes de vincular.';
  return 'El envío requiere revisión. Comprueba sus datos con el responsable de la integración A+W antes de vincular.';
}
