/** Traduce el bloqueo de identidad sin cambiar la validación del servidor. */
export function motivoVinculacionOtr(motivo: string | null): string | null {
  if (!motivo) return null;
  if (motivo === 'CE_OTR_SUCURSAL_DESCONOCIDA')
    return 'Sucursal sin correspondencia en el ERP. Revisa la equivalencia con A+W antes de vincular.';
  if (motivo === 'CE_OTR_CLIENTE_DESCONOCIDO')
    return 'Cliente sin correspondencia en el ERP. Revisa la equivalencia con A+W antes de vincular.';
  return 'El envío requiere revisión. Comprueba sus datos con el responsable de la integración A+W antes de vincular.';
}

/** Texto de una partida OTR: los avisos conocidos se explican; el resto conserva el texto genérico. */
export function motivoLineaOtr(motivo: string | null): string | null {
  if (!motivo) return null;
  const codigo = motivo.split(':')[0];
  if (codigo === 'CE_OTR_PEDIDO_INEXISTENTE')
    return 'El pedido no está registrado. Revisa el número de pedido en A+W.';
  if (codigo === 'CE_OTR_CANTIDAD_CERO')
    return 'OTR mandó la posición sin piezas; se omitió y no detiene el resto del envío.';
  if (codigo === 'CE_OTR_HISTORICO_SAP')
    return 'El pedido ya se facturó en SAP; la partida queda como consulta y no se factura en el ERP.';
  if (codigo === 'CE_OTR_POSICION_INEXISTENTE')
    return 'La posición no existe en el pedido del ERP. Revisa el pedido en A+W.';
  return 'La partida requiere revisión. Comprueba sus datos con A+W antes de vincularla.';
}

/** Motivo de rechazo de una entrada OTR (código guardado por el lector). */
export function motivoRechazoOtr(motivo: string | null): string {
  if (!motivo) return 'Sin motivo registrado.';
  if (motivo === 'CE_OTR_DATOS_INVALIDOS')
    return 'Datos inválidos: cliente, sucursal, entrega o partidas (cantidad negativa o repetida).';
  if (motivo === 'CE_OTR_VARIOS_CLIENTES') return 'La pirámide trae piezas de varios clientes.';
  if (motivo === 'CE_OTR_VARIAS_SUCURSALES') return 'La pirámide trae piezas de varias sucursales.';
  if (motivo.startsWith('CE_OTR_ERROR_TECNICO'))
    return 'Error técnico al leer A+W tras varios intentos. Avisa al responsable de la integración.';
  return motivo;
}
