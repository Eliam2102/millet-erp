import { describe, expect, it } from 'vitest';
import { rutaPermitida } from '@/lib/nav';

// Rutas de _app sin beforeLoad propio: solo las protege la guarda de _app
// (rutaPermitida). Sin permisos, todas deben rechazarse salvo Inicio.
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

  it.each(rutasSinGuarda)('%s: rutaPermitida con permisos vacíos', (ruta) => {
    expect(rutaPermitida(ruta, [])).toBe(ruta === '/');
  });
});
