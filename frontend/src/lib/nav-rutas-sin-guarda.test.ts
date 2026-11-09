import { describe, expect, it } from 'vitest';
import { rutaPermitida, rutasSoloSesion } from '@/lib/nav';

// Rutas de _app sin beforeLoad propio: solo las protege la guarda de _app
// (rutaPermitida). Sin permisos, todas deben rechazarse salvo Inicio y las que solo piden sesión.
const fuentes = import.meta.glob('/src/routes/_app/**/*.tsx', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const rutasSinGuarda = Object.entries(fuentes)
  .filter(([archivo, src]) => !/\.test\.tsx$/.test(archivo) && !src.includes('beforeLoad'))
  .map(([archivo]) => {
    const ruta = archivo
      .replace('/src/routes/_app', '')
      .replace(/\.tsx$/, '')
      .replace(/\/index$/, '')
      .replace(/\$[A-Za-z]+/g, 'ejemplo')
      .replace(/\/+$/, '');
    return ruta === '' ? '/' : ruta;
  })
  .sort();

describe('rutas de _app sin beforeLoad propio', () => {
  it('encuentra las rutas', () => {
    expect(rutasSinGuarda.length).toBeGreaterThan(0);
  });

  const soloSesion = (ruta: string) => rutasSoloSesion.some((r) => ruta === r || ruta.startsWith(`${r}/`));

  it.each(rutasSinGuarda)('%s: rutaPermitida con permisos vacíos', (ruta) => {
    expect(rutaPermitida(ruta, [])).toBe(ruta === '/' || soloSesion(ruta));
  });
});

// B10: el responsable de una incidencia sin permisos de incidencias abre «Mis incidencias» y el detalle de la suya.
describe('«Mis incidencias» sin permisos de incidencias', () => {
  it.each(['/incidencias', '/incidencias/019e0000-0000-7000-8000-000000000001'])('%s se permite con sesión', (ruta) => {
    expect(rutaPermitida(ruta, ['facturacion.pedidos.leer'])).toBe(true);
  });

  it('no abre las incidencias de Comercio Exterior', () => {
    expect(rutaPermitida('/comercio-exterior/incidencias', [])).toBe(false);
  });
});
