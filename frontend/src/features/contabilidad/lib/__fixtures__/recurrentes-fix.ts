/** Fixtures de pólizas recurrentes para pruebas (datos FIX, sin datos reales). */
export function partidaPlantilla(o: Record<string, unknown> = {}) {
  return {
    cuentaId: 'c1', concepto: null, cargo: 5000, abono: 0, sucursalId: 's1', dim1Id: null, dim2Id: null, dim3Id: null, proyecto: null,
    clienteId: null, proveedorId: null, cuentaBancariaId: null, moneda: null, tipoCambio: null, importeOriginal: null, ...o,
  };
}

export function aplicacion(o: Record<string, unknown> = {}) {
  return {
    anio: 2026, mes: 10, fecha: '2026-10-31', polizaId: null, folio: null, estado: null, fechaReversa: '2026-11-01', reversaId: null,
    reversaFolio: null, ...o,
  };
}

export function plantilla(o: Record<string, unknown> = {}) {
  return {
    id: 'r1', nombre: 'FIX Provisión de renta', tipoOperacionId: 't1', tipoClave: 'TT', concepto: 'FIX Provisión mensual de renta',
    anioInicio: 2026, mesInicio: 9, numeroPeriodos: 3, diaDelMes: 0, reversaAutomatica: true, naceAutorizada: false, estado: 'Activa',
    importe: 5000,
    partidas: [partidaPlantilla(), partidaPlantilla({ cuentaId: 'c2', cargo: 0, abono: 5000 })],
    creadaPorId: 'u-analista', creadaPor: 'FIX Analista', creadaEn: '2026-09-01T15:00:00Z', activadaPorId: 'u-super',
    activadaPor: 'FIX Supervisor', activadaEn: '2026-09-02T15:00:00Z', generadas: 1,
    aplicaciones: [
      aplicacion({ mes: 9, fecha: '2026-09-30', polizaId: 'p10', folio: 'TT-000130', estado: 'Autorizada', fechaReversa: '2026-10-01', reversaId: 'p11', reversaFolio: 'TT-000131' }),
      aplicacion(),
      aplicacion({ mes: 11, fecha: '2026-11-30', fechaReversa: '2026-12-01' }),
    ],
    version: 2, ...o,
  };
}

export function resumenPlantilla(o: Record<string, unknown> = {}) {
  return {
    id: 'r1', nombre: 'FIX Provisión de renta', tipoClave: 'TT', anioInicio: 2026, mesInicio: 9, numeroPeriodos: 3, reversaAutomatica: true,
    naceAutorizada: false, estado: 'Activa', importe: 5000, generadas: 1, creadaPor: 'FIX Analista', version: 2, ...o,
  };
}
