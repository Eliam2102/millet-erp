import { describe, expect, it } from 'vitest';
import {
  calcularRevisionPrevia,
  motivoTransmisionBloqueada,
} from './revision-previa-oc';

const tipos = [
  { id: 't-cot', clave: 'cotizacion' },
  { id: 't-correo', clave: 'correo_autorizacion' },
  { id: 't-ficha', clave: 'ficha_tecnica' },
];

const base = {
  camposPorCapturar: [],
  lineas: [{ posicion: 1, precioUnitario: 10 }],
  adjuntos: [{ tipoDocumentoId: 't-cot' }],
  sinRequisicionPrevia: false,
  motivoSinRequisicion: null,
  esImportacion: false,
  infoImportIncotermId: null,
  infoImportPaisOrigen: null,
};
type Oc = Parameters<typeof calcularRevisionPrevia>[0];
const oc = (o: Record<string, unknown> = {}) => ({ ...base, ...o }) as unknown as Oc;
const req = (r: ReturnType<typeof calcularRevisionPrevia>, id: string) =>
  r.requisitos.find((x) => x.id === id);

describe('calcularRevisionPrevia', () => {
  it('OC completa: sin pendientes', () => {
    const r = calcularRevisionPrevia(oc(), tipos);
    expect(r.pendientes).toBe(0);
    expect(r.requisitos.map((x) => x.id)).toEqual(['cabecera', 'precios', 'cotizacion']);
  });

  it('cabecera con campos por capturar', () => {
    const r = calcularRevisionPrevia(oc({ camposPorCapturar: ['proveedor', 'usoPrincipal'] }), tipos);
    expect(req(r, 'cabecera')).toMatchObject({
      ok: false,
      detalle: 'Falta: proveedor, uso principal.',
      destino: { tab: 'informacion' },
    });
  });

  it('precio ≤ 0 o sin líneas bloquea y apunta a la primera línea', () => {
    const r = calcularRevisionPrevia(
      oc({ lineas: [{ posicion: 3, precioUnitario: 0 }, { posicion: 2, precioUnitario: -1 }, { posicion: 1, precioUnitario: 5 }] }),
      tipos,
    );
    expect(req(r, 'precios')).toMatchObject({
      ok: false,
      detalle: 'Faltan los precios de las líneas 2, 3.',
      destino: { tab: 'lineas', etiqueta: 'Ir a línea 2' },
    });
    expect(req(calcularRevisionPrevia(oc({ lineas: [{ posicion: 1, precioUnitario: 0 }] }), tipos), 'precios')?.detalle).toBe('Falta el precio de la línea 1.');
    expect(req(calcularRevisionPrevia(oc({ lineas: [] }), tipos), 'precios')?.ok).toBe(false);
  });

  it('cotización faltante apunta a Adjuntos; el correo no la sustituye', () => {
    const r = calcularRevisionPrevia(oc({ adjuntos: [{ tipoDocumentoId: 't-correo' }] }), tipos);
    expect(req(r, 'cotizacion')).toMatchObject({ ok: false, destino: { tab: 'adjuntos' } });
  });

  it('sin RQ exige motivo y correo', () => {
    const falta = calcularRevisionPrevia(oc({ sinRequisicionPrevia: true, motivoSinRequisicion: '  ' }), tipos);
    expect(req(falta, 'motivoSinRq')?.ok).toBe(false);
    expect(req(falta, 'correoAutorizacion')?.ok).toBe(false);
    const ok = calcularRevisionPrevia(
      oc({ sinRequisicionPrevia: true, motivoSinRequisicion: 'Urgente', adjuntos: [{ tipoDocumentoId: 't-cot' }, { tipoDocumentoId: 't-correo' }] }),
      tipos,
    );
    expect(ok.pendientes).toBe(0);
    expect(req(calcularRevisionPrevia(oc(), tipos), 'correoAutorizacion')).toBeUndefined();
  });

  it('importación exige ficha técnica, incoterm y país', () => {
    const falta = calcularRevisionPrevia(oc({ esImportacion: true, infoImportIncotermId: 'i' }), tipos);
    expect(req(falta, 'fichaTecnica')?.ok).toBe(false);
    expect(req(falta, 'importacion')).toMatchObject({ ok: false, detalle: 'Falta: país de origen.' });
    const ok = calcularRevisionPrevia(
      oc({ esImportacion: true, infoImportIncotermId: 'i', infoImportPaisOrigen: 'CN', adjuntos: [{ tipoDocumentoId: 't-cot' }, { tipoDocumentoId: 't-ficha' }] }),
      tipos,
    );
    expect(ok.pendientes).toBe(0);
  });

  it('sin catálogo de tipos omite los requisitos de adjuntos', () => {
    const r = calcularRevisionPrevia(oc({ adjuntos: [] }), undefined);
    expect(req(r, 'cotizacion')).toBeUndefined();
    expect(r.pendientes).toBe(0);
  });
});

describe('motivoTransmisionBloqueada', () => {
  it('describe cuántos faltan o devuelve undefined', () => {
    expect(motivoTransmisionBloqueada({ pendientes: 2, total: 5 })).toBe(
      'Faltan 2 de 5 requisitos · ver revisión previa',
    );
    expect(motivoTransmisionBloqueada({ pendientes: 0, total: 5 })).toBeUndefined();
  });
});

describe('calcularRevisionPrevia · medida de hoja', () => {
  it('línea de vidrio sin medida bloquea y apunta a esa línea', () => {
    const r = calcularRevisionPrevia(
      oc({ lineas: [{ posicion: 1, precioUnitario: 10, requiereMedida: true }, { posicion: 2, precioUnitario: 10 }] }),
      tipos,
    );
    expect(r.pendientes).toBe(1);
    expect(req(r, 'medidaHoja')).toMatchObject({
      ok: false,
      detalle: 'Falta la medida de la línea 1.',
      destino: { tab: 'lineas', etiqueta: 'Ir a línea 1' },
    });
  });
});
