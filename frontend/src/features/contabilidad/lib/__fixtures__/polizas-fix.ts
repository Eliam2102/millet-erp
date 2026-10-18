/** Fixtures de pólizas para pruebas (datos FIX, sin datos reales). */
export function partida(o: Record<string, unknown> = {}) {
  return {
    id: 'pa1', linea: 1, cuentaId: 'c1', cuentaCodigo: 'FIX-110', cuentaNombre: 'FIX Caja', concepto: null, cargo: 1234, abono: 0,
    moneda: 'MXN', tipoCambio: 1, importeOriginal: null, sucursalId: 's1', sucursalNombre: 'FIX Mérida',
    dim1: null, dim2: null, dim3: null, proyecto: null, cliente: null, proveedor: null, cuentaBancaria: null, ...o,
  };
}

export function poliza(o: Record<string, unknown> = {}) {
  return {
    id: 'p1', folio: 'TT-000123', tipoOperacionId: 't1', tipoClave: 'TT', tipoNombre: 'FIX Traspasos', tipoSat: 'Diario', numero: 123,
    fecha: '2026-10-05', periodoAnio: 2026, periodoNumero: 10, concepto: 'FIX Reclasificación', origen: 'Manual', estado: 'Autorizada',
    documentoOrigen: null, polizaAnuladaId: null, polizaAnuladaFolio: null, anuladaPorPolizaId: null, anuladaPorFolio: null,
    motivoAnulacion: null, anuladaPor: null, anuladaEn: null, capturadaPorId: 'u-analista', capturadaPor: 'FIX Analista',
    capturadaEn: '2026-10-05T15:00:00Z', autorizadaPorId: 'u-super', autorizadaPor: 'FIX Supervisor', autorizadaEn: '2026-10-06T15:00:00Z',
    totalCargos: 1234, totalAbonos: 1234, diferencia: 0, cuadra: true,
    partidas: [partida(), partida({ id: 'pa2', linea: 2, cuentaId: 'c2', cuentaCodigo: 'FIX-120', cuentaNombre: 'FIX Bancos', cargo: 0, abono: 1234 })],
    version: 4, ...o,
  };
}

export function resumen(o: Record<string, unknown> = {}) {
  return {
    id: 'p1', folio: 'TT-000123', tipoOperacionId: 't1', tipoClave: 'TT', numero: 123, fecha: '2026-10-05', periodoAnio: 2026,
    periodoNumero: 10, concepto: 'FIX Reclasificación', origen: 'Manual', estado: 'Autorizada', esAnulacion: false,
    totalCargos: 1234, totalAbonos: 1234, capturadaPor: 'FIX Analista', autorizadaPor: 'FIX Supervisor', version: 4, ...o,
  };
}
