import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { leerTamanoPagina, useTamanoPagina } from './useTamanoPagina';
import { usePaginacionLocal } from './usePaginacionLocal';

describe('cantidad de registros', () => {
  beforeEach(() => localStorage.clear());
  it('descarta preferencias corruptas y fuera de contrato', () => {
    for (const valor of ['NaN', '-1', '0', '201', '20.5', '{}']) {
      localStorage.setItem('tabla', valor);
      expect(leerTamanoPagina('tabla')).toBe('auto');
    }
  });
  it('recuerda cantidad al desmontar y volver', () => {
    const a = renderHook(() => useTamanoPagina());
    act(() => a.result.current.elegir(20));
    expect(a.result.current.limit).toBe(20);
    a.unmount();
    const b = renderHook(() => useTamanoPagina());
    expect(b.result.current.limit).toBe(20);
  });
  it('pagina, reinicia filtros/tamaño y recorta al reducir resultados', () => {
    const items = Array.from({ length: 56 }, (_, i) => i);
    const h = renderHook(({ filtro, datos }) => usePaginacionLocal(datos, filtro), {
      initialProps: { filtro: '', datos: items },
    });
    act(() => h.result.current.onChange(0, 20));
    act(() => h.result.current.onChange(40, 20));
    expect(h.result.current.visibles).toHaveLength(16);
    h.rerender({ filtro: '', datos: items.slice(0, 12) });
    expect(h.result.current.offset).toBe(0);
    h.rerender({ filtro: 'nuevo', datos: items });
    expect(h.result.current.offset).toBe(0);
    h.rerender({ filtro: '', datos: items });
    expect(h.result.current.offset).toBe(0);
  });
  it('ajusta a la región disponible y respeta una elección explícita al redimensionar', () => {
    const region = document.createElement('div');
    region.setAttribute('data-table-viewport', '');
    document.body.append(region);
    let alto = 480;
    Object.defineProperty(region, 'clientHeight', { get: () => alto });
    const h = renderHook(() => useTamanoPagina());
    expect(h.result.current.limit).toBe(10);
    alto = 920;
    act(() => window.dispatchEvent(new Event('resize')));
    expect(h.result.current.limit).toBe(20);
    act(() => h.result.current.elegir(50));
    alto = 480;
    act(() => window.dispatchEvent(new Event('resize')));
    expect(h.result.current.limit).toBe(50);
    region.remove();
  });
  it('mide la región cuando aparece después de cargar datos sin perder modo automático', () => {
    const region = document.createElement('div');
    region.id = 'region-carga-asincrona';
    region.setAttribute('data-table-viewport', '');
    Object.defineProperty(region, 'clientHeight', { value: 920 });
    const h = renderHook(() => useTamanoPagina('carga-asincrona', 100, 10, region.id));
    expect(h.result.current.limit).toBe(10);
    document.body.append(region);
    h.rerender();
    expect(h.result.current.limit).toBe(20);
    expect(h.result.current.modo).toBe('auto');
    region.remove();
  });
  it('permite elegir aunque el almacenamiento esté bloqueado', () => {
    const stub = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denegado');
    });
    const h = renderHook(() => useTamanoPagina());
    act(() => h.result.current.elegir(50));
    expect(h.result.current.limit).toBe(50);
    stub.mockRestore();
  });
  it('congela la cantidad durante una edición para no desmontar lo capturado al redimensionar', () => {
    const region = document.createElement('div');
    region.id = 'region-edicion';
    region.setAttribute('data-table-viewport', '');
    document.body.append(region);
    let alto = 480;
    Object.defineProperty(region, 'clientHeight', { get: () => alto });
    const h = renderHook(() => useTamanoPagina('edicion', 100, 10, region.id));
    expect(h.result.current.limit).toBe(10);
    const form = document.createElement('form');
    const input = document.createElement('input');
    input.value = '[CAPTURA FICTICIA]';
    form.append(input);
    region.append(form);
    act(() => window.dispatchEvent(new Event('resize')));
    alto = 920;
    act(() => window.dispatchEvent(new Event('resize')));
    expect(h.result.current.editando).toBe(true);
    expect(h.result.current.limit).toBe(10);
    expect(input.value).toBe('[CAPTURA FICTICIA]');
    form.remove();
    act(() => window.dispatchEvent(new Event('resize')));
    expect(h.result.current.limit).toBe(20);
    region.remove();
  });
});
