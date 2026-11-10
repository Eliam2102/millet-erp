import { describe, expect, it } from 'vitest';
import {
  nombreDeDisposicion,
  nombreSatArchivoCe,
  rutaArchivoCe,
  type RevisionContabilidadElectronica,
} from '../api/contabilidad-electronica';
import { motivoArchivoCe, resumenProblemasCe } from './contabilidad-electronica';

const revision: RevisionContabilidadElectronica = {
  anio: 2027,
  mes: 1,
  rfc: 'MIL010101AB1',
  rfcValido: true,
  estadoPeriodo: 'Cerrado',
  cuentasCatalogo: 6,
  problemas: [],
  puedeGenerarCatalogo: true,
  polizas: 3,
  polizasConCfdi: 1,
  avisos: [],
  cuentasConProblemas: 0,
};

const base = { anio: 2027, mes: 1, formato: 'zip' as const };

describe('contabilidad electrónica (K10.8)', () => {
  it('H1: sin Content-Disposition legible, el nombre del SAT se arma con el RFC (RFC+AAAAMM+CT/BN/BC/PL)', () => {
    const p = { anio: 2026, mes: 11, formato: 'zip' as const, rfc: 'min120224147' };
    expect(nombreSatArchivoCe({ ...p, tipo: 'catalogo' })).toBe('MIN120224147202611CT.zip');
    expect(nombreSatArchivoCe({ ...p, tipo: 'balanza', tipoEnvio: 'N' })).toBe('MIN120224147202611BN.zip');
    expect(nombreSatArchivoCe({ ...p, tipo: 'balanza', tipoEnvio: 'C' })).toBe('MIN120224147202611BC.zip');
    expect(nombreSatArchivoCe({ ...p, tipo: 'polizas', formato: 'xml' })).toBe('MIN120224147202611PL.xml');
    expect(nombreSatArchivoCe({ ...p, tipo: 'catalogo', rfc: null })).toBe('contabilidad-electronica-catalogo-2026-11.zip');
  });

  it('O2: cuenta cuentas distintas, como el bloqueo del servidor, y aclara cuando una cuenta tiene dos pendientes', () => {
    const p = (cuentaId: string, problema: string) => ({ cuentaId, codigo: cuentaId, nombre: cuentaId, codigoAgrupador: null, problema });
    const dos = [p('a', 'Sin código agrupador'), p('a', 'Sin naturaleza'), p('b', 'Sin código agrupador')];
    expect(resumenProblemasCe({ problemas: dos, cuentasConProblemas: 2 })).toBe('2 cuentas con 3 pendientes');
    expect(resumenProblemasCe({ problemas: [p('b', 'Sin naturaleza')], cuentasConProblemas: 1 })).toBe('1 cuenta');
    expect(resumenProblemasCe({ problemas: dos, cuentasConProblemas: 0 })).toBe('2 cuentas con 3 pendientes');
  });

  it('K10.8-a: con cuentas pendientes el catálogo no se descarga, la balanza sí', () => {
    const r = { ...revision, puedeGenerarCatalogo: false };
    expect(motivoArchivoCe('catalogo', r, { ...base, tipo: 'catalogo' })).toMatch(/sin código agrupador/);
    expect(motivoArchivoCe('balanza', r, { ...base, tipo: 'balanza', tipoEnvio: 'N' })).toBeUndefined();
  });

  it('sin RFC válido no se genera nada; el periodo 13 solo da la balanza', () => {
    expect(motivoArchivoCe('balanza', { ...revision, rfcValido: false }, { ...base, tipo: 'balanza' })).toMatch(/RFC/);
    const cierre = { ...revision, mes: 13 };
    expect(motivoArchivoCe('polizas', cierre, { ...base, tipo: 'polizas' })).toMatch(/periodo 13/);
    expect(motivoArchivoCe('balanza', cierre, { ...base, tipo: 'balanza', tipoEnvio: 'N' })).toBeUndefined();
  });

  it('las pólizas piden número de orden (AF/FC) o de trámite (DE/CO) con la forma del SAT', () => {
    const p = { ...base, tipo: 'polizas' as const };
    expect(motivoArchivoCe('polizas', revision, { ...p, solicitud: 'AF', numOrden: '123' })).toMatch(/número de orden/);
    expect(motivoArchivoCe('polizas', revision, { ...p, solicitud: 'AF', numOrden: 'ABC1234567/12' })).toBeUndefined();
    expect(motivoArchivoCe('polizas', revision, { ...p, solicitud: 'DE', numTramite: '' })).toMatch(/número de trámite/);
    expect(motivoArchivoCe('polizas', { ...revision, polizas: 0 }, { ...p, solicitud: 'DE', numTramite: 'AB123456789012' })).toMatch(
      /no tiene pólizas/,
    );
  });

  it('la ruta lleva solo los parámetros del archivo y el nombre sale del servidor', () => {
    expect(rutaArchivoCe({ ...base, tipo: 'balanza', tipoEnvio: 'C', fechaModificacion: '2027-03-05', solicitud: 'AF' })).toBe(
      '/api/v1/contabilidad/contabilidad-electronica/balanza?anio=2027&mes=1&formato=zip&tipoEnvio=C&fechaModificacion=2027-03-05',
    );
    expect(rutaArchivoCe({ ...base, tipo: 'polizas', solicitud: 'CO', numOrden: 'X', numTramite: 'AB123456789012' })).toBe(
      '/api/v1/contabilidad/contabilidad-electronica/polizas?anio=2027&mes=1&formato=zip&solicitud=CO&numTramite=AB123456789012',
    );
    expect(nombreDeDisposicion('attachment; filename=MIL010101AB1202701CT.zip; filename*=UTF-8\'\'MIL010101AB1202701CT.zip', 'x')).toBe(
      'MIL010101AB1202701CT.zip',
    );
    expect(nombreDeDisposicion(null, 'respaldo.zip')).toBe('respaldo.zip');
  });
});
