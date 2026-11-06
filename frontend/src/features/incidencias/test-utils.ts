import { useAuthStore } from '@/lib/auth/auth-store';
import type { IncidenciaDetalle, IncidenciaFila } from '@/features/incidencias/api/types';

export const USUARIO_ID = 'u-actual';

export function sesion(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: USUARIO_ID, email: 't@e.com', nombre: 'Usuario de prueba' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

export function cerrarSesion() {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
}

export function fila(parcial: Partial<IncidenciaFila> = {}): IncidenciaFila {
  return {
    id: 'inc-1',
    numero: 12,
    modulo: 'almacen',
    tipoDocumento: 'recepcion',
    documentoId: 'doc-1',
    documentoEtiqueta: 'REC-000123 · OC-000045',
    sucursalId: null,
    paso: 'alm.recepcion.diferencia-oc',
    pasoNombre: 'Diferencia de recepción contra la orden de compra',
    codigoMotivo: 'RECEPCION_EXCEDE_PENDIENTE',
    motivo: 'Llegaron 12 piezas y la orden solo tiene 10 pendientes.',
    estado: 'Abierta',
    responsableUsuarioId: USUARIO_ID,
    responsableNombre: 'Usuario de prueba',
    abiertaPorNombre: 'Sistema',
    automatica: true,
    abiertaEn: '2026-11-05T18:00:00Z',
    fechaCompromiso: '2026-11-07',
    rutaReanudacion: '/almacen/recepciones/doc-1',
    version: 1,
    ...parcial,
  };
}

export function detalle(parcial: Partial<IncidenciaDetalle> = {}): IncidenciaDetalle {
  return {
    ...fila(),
    detalle: null,
    detalleClave: null,
    pasoReanudacion: 'alm.recepcion.registrar',
    pasoReanudacionNombre: 'Registrar recepción',
    abiertaPorId: null,
    resueltaPorNombre: null,
    resueltaEn: null,
    resolucion: null,
    puedeAtender: true,
    puedeResolver: true,
    puedeCancelar: false,
    puedeReasignar: true,
    historial: [
      { en: '2026-11-05T18:00:00Z', accion: 'Abierta', estadoAnterior: null, estadoNuevo: 'Abierta', usuarioNombre: 'Sistema', nota: null },
    ],
    ...parcial,
  };
}
