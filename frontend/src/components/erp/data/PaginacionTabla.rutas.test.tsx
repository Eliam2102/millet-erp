import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PaginacionTabla } from './PaginacionTabla';
import { usePaginacionLocal } from './usePaginacionLocal';

beforeEach(() => {
  localStorage.clear();
  useAuthStore.setState({ user: null });
  window.history.replaceState({}, '', '/prueba-paginacion');
  localStorage.setItem('millet:tabla:v1:local:/prueba-paginacion:principal', '20');
});
afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
  window.history.replaceState({}, '', '/');
});

it('respeta otra cantidad explícita al volver por SPA sin desmontar el paginador', () => {
  const onChange = vi.fn();
  const view = render(<PaginacionTabla total={200} offset={0} limit={20} onChange={onChange} />);
  expect(onChange).not.toHaveBeenCalled();
  window.history.replaceState({}, '', '/prueba-paginacion?page=2&pageSize=10');
  view.rerender(<PaginacionTabla total={200} offset={10} limit={10} onChange={onChange} />);
  expect(screen.getByLabelText('Filas por página')).toHaveValue('10');
  expect(screen.getByText('11–20 de 200')).toBeInTheDocument();
  expect(onChange).not.toHaveBeenCalled();
});

it('mantiene Automático cuando sus propios ajustes cambian la URL', async () => {
  let alto = 480;
  window.history.replaceState({}, '', '/prueba-paginacion?pageSize=20');
  function Listado() {
    const [limit, setLimit] = useState(20);
    return <>
      <div id="region-ruta-auto" data-table-viewport ref={el => {
        if (el) Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => alto });
      }}><div data-fila-registro>Muestra</div></div>
      <PaginacionTabla regionId="region-ruta-auto" total={200} offset={0} limit={limit}
        onChange={(_, cantidad) => {
          window.history.replaceState({}, '', `/prueba-paginacion?pageSize=${cantidad}`);
          setLimit(cantidad);
        }} />
    </>;
  }
  render(<Listado />);
  fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: 'auto' } });
  await waitFor(() => expect(window.location.search).toBe('?pageSize=10'));
  expect(screen.getByLabelText('Filas por página')).toHaveValue('auto');
  alto = 920;
  act(() => window.dispatchEvent(new Event('resize')));
  await waitFor(() => expect(window.location.search).toBe('?pageSize=20'));
  expect(screen.getByLabelText('Filas por página')).toHaveValue('auto');
});


it('no convierte Automático en un número cuando el observador del pie se adelanta al del listado', () => {
  localStorage.setItem('millet:tabla:v1:local:/prueba-paginacion:principal', 'auto');
  let alto = 480;
  const observadores: { callback: ResizeObserverCallback; target?: Element; activo: boolean }[] = [];
  vi.stubGlobal('ResizeObserver', class {
    registro: typeof observadores[number];
    constructor(callback: ResizeObserverCallback) {
      this.registro = { callback, activo: true };
      observadores.push(this.registro);
    }
    observe(target: Element) { this.registro.target = target; }
    disconnect() { this.registro.activo = false; }
  });
  const datos = Array.from({ length: 60 }, (_, i) => i);
  function Listado() {
    const pagina = usePaginacionLocal(datos);
    return <>
      <div id={pagina.regionId} data-table-viewport ref={el => {
        if (el) Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => alto });
      }}>
        {pagina.visibles.map(id => <div data-testid="fila" data-fila-registro key={id}>{id}</div>)}
      </div>
      <PaginacionTabla regionId={pagina.regionId} total={datos.length} offset={pagina.offset}
        limit={pagina.limit} onChange={pagina.onChange} />
    </>;
  }
  render(<Listado />);
  const activos = observadores.filter(o => o.activo && o.target?.hasAttribute('data-table-viewport'));
  expect(activos).toHaveLength(2);
  alto = 920;
  act(() => activos[0].callback([], {} as ResizeObserver));
  expect(screen.getByLabelText('Filas por página')).toHaveValue('auto');
  expect(activos[1].activo).toBe(true);
  act(() => activos[1].callback([], {} as ResizeObserver));
  expect(screen.getAllByTestId('fila')).toHaveLength(20);
  expect(screen.getByLabelText('Filas por página')).toHaveValue('auto');
  expect(localStorage.getItem('millet:tabla:v1:local:/prueba-paginacion:principal')).toBe('auto');
});
