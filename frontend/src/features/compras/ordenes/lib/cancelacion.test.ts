import { describe, expect, it } from 'vitest';
import { puedeResolverCancelacion } from './cancelacion';
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
