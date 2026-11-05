import { describe, expect, it } from 'vitest';
import { avisoCancelacion, estadoSolicitud, puedeResolverCancelacion, respuestasAlmacenes, TOAST_CANCELACION_REGISTRADA } from './cancelacion';
import { EstadoOrdenCompra, type SolicitudCancelacionOc } from '../api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { accionCancelar1Firma, accionCancelarDobleFirma } from './acciones-disponibles';
import { CancelarOcConRecepcionesSchema, ResolverCancelacionOcSchema } from '../schemas/cancelar-doble-firma';

const firma: SolicitudCancelacionOc = {
  id: 'solicitud', solicitanteId: 'jefe', fechaSolicitud: '2026-10-09T12:00:00Z',
  motivoCancelacionId: '00000000-0000-0000-0000-000000000001', motivoSolicitud: 'Cancelar faltante',
  resolutorId: null, fechaResolucion: null, confirmada: null, motivoResolucion: null,
};
const n2 = [PermisosCanonicos.ComprasOrdenesAutorizarNivel2];
describe('Cancelación P2', () => {
  it('solo Dirección con otra persona puede resolver', () => {
    expect(puedeResolverCancelacion(7, [firma], 'direccion', n2)).toBe(true);
    expect(puedeResolverCancelacion(7, [firma], 'jefe', Object.values(PermisosCanonicos))).toBe(false);
    expect(puedeResolverCancelacion(7, [firma], 'direccion', [])).toBe(false);
    expect(puedeResolverCancelacion(3, [firma], 'direccion', n2)).toBe(false);
    expect(puedeResolverCancelacion(7, [], 'direccion', n2)).toBe(false);
  });
  it('N1 solicita sin necesitar permiso N2; no hay cancelación simple pendiente', () => {
    expect(accionCancelarDobleFirma({ estado: 3, subEstadoRecepcion: 1 }, [PermisosCanonicos.ComprasOrdenesCancelarDoble, PermisosCanonicos.ComprasOrdenesAutorizarNivel1]).habilitada).toBe(true);
    expect(accionCancelarDobleFirma({ estado: 3, subEstadoRecepcion: 1 }, [PermisosCanonicos.ComprasOrdenesCancelarDoble]).visible).toBe(false);
    expect(accionCancelar1Firma({ estado: EstadoOrdenCompra.CancelacionSolicitada, subEstadoRecepcion: 1 }, Object.values(PermisosCanonicos)).visible).toBe(false);
  });
  it('las dos firmas requieren motivo y aceptan ids del seed', () => {
    expect(CancelarOcConRecepcionesSchema.safeParse({ motivoCancelacionId: firma.motivoCancelacionId, motivoCancelacionTexto: 'Motivo' }).success).toBe(true);
    expect(CancelarOcConRecepcionesSchema.safeParse({ motivoCancelacionId: firma.motivoCancelacionId, motivoCancelacionTexto: '  ' }).success).toBe(false);
    expect(ResolverCancelacionOcSchema.safeParse({ confirmar: false, motivo: '' }).success).toBe(false);
    expect(ResolverCancelacionOcSchema.safeParse({ confirmar: true, motivo: 'Confirmación' }).success).toBe(true);
  });
});

describe('Cancelación condicionada a Almacenes (86e3jvu83)', () => {
  const espera: SolicitudCancelacionOc = { ...firma, esperaAlmacenes: true };
  const aceptada: SolicitudCancelacionOc = { ...firma, fechaResolucion: '2026-11-04T13:00:00Z', confirmada: true, esperaAlmacenes: false };
  const rechazada: SolicitudCancelacionOc = {
    ...firma, fechaResolucion: '2026-11-04T13:00:00Z', confirmada: false, esperaAlmacenes: false,
    respuestaAlmacenes: 'ya tiene entrada en Almacenes; lo sobrante sale como salida manual',
  };

  it('(1) una solicitud resuelta por Almacenes ya no es «Pendiente de Dirección»', () => {
    expect(estadoSolicitud(firma).texto).toBe('Pendiente de Dirección');
    expect(estadoSolicitud(espera)).toMatchObject({ clave: 'esperaAlmacenes', texto: 'Esperando respuesta de Almacenes' });
    expect(estadoSolicitud(aceptada)).toMatchObject({ clave: 'aceptadaAlmacenes', texto: 'Aceptada por Almacenes' });
    expect(estadoSolicitud(rechazada)).toMatchObject({ clave: 'rechazadaAlmacenes', texto: 'Rechazada por Almacenes' });
    expect(estadoSolicitud({ ...firma, resolutorId: 'direccion', fechaResolucion: '2026-10-09T13:00:00Z', confirmada: true }).texto).toBe('Cancelación confirmada');
  });

  it('nadie resuelve a mano una cancelación que espera a Almacenes', () => {
    expect(puedeResolverCancelacion(7, [espera], 'direccion', n2)).toBe(false);
    expect(puedeResolverCancelacion(7, [rechazada], 'direccion', n2)).toBe(false);
  });

  it('(2) el aviso de Almacenes sale de respuestaAlmacenes', () => {
    expect(respuestasAlmacenes([firma, rechazada])).toEqual(['ya tiene entrada en Almacenes; lo sobrante sale como salida manual']);
    expect(respuestasAlmacenes([firma, aceptada])).toEqual([]);
  });

  it('(3) la redacción distingue Almacenes de Dirección y no dice «cancelada»', () => {
    const almacenes = avisoCancelacion(EstadoOrdenCompra.CancelacionSolicitada, [espera]);
    expect(almacenes).toContain('Almacenes');
    expect(almacenes).not.toContain('Dirección');
    expect(avisoCancelacion(EstadoOrdenCompra.CancelacionSolicitada, [firma])).toContain('Dirección');
    expect(avisoCancelacion(EstadoOrdenCompra.Autorizada, [rechazada])).toBeNull();
    expect(TOAST_CANCELACION_REGISTRADA('OC-1')).not.toMatch(/ cancelada\./);
  });
});
