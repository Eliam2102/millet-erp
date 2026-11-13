import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PaginacionTabla } from './PaginacionTabla';

beforeEach(() => {
  localStorage.clear();
  useAuthStore.setState({ user: null });
  window.history.replaceState({}, '', '/prueba-paginacion');
  localStorage.setItem('millet:tabla:v1:local:/prueba-paginacion:principal', '20');
});
afterEach(() => {
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
      }} />
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
