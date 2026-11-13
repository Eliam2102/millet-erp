import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CampanaAvisos } from './CampanaAvisos';
import { destinoInterno } from '../api/destinoInterno';
const hook = vi.hoisted(() => vi.fn());
vi.mock('../api/useCentroAvisos', () => ({ useCentroAvisos: (filtros: unknown) => hook(filtros) }));
vi.mock('@tanstack/react-router', () => ({ Link: ({ children, to, onClick }: { children: React.ReactNode; to: string; onClick: () => void }) => <a href={to} onClick={onClick}>{children}</a> }));
const leer = vi.fn();
const base = { isPending: false, isError: false, leer: { mutate: leer, isPending: false }, cambiarSonido: { mutate: vi.fn(), isPending: false }, probarSonido: vi.fn(), refetch: vi.fn(), sonidoHabilitado: false };
const data = { coberturaCompleta: false, fuentes: [{ id: 'compras', estado: 'Complete' }, { id: 'almacen', estado: 'Failed' }], totalSinLeerConocido: 32, totalAccionesConocidas: 45, totalFiltradoConocido: 50, siguienteContinuacion: 'p2', items: [{ leido: false, aviso: { claveOcurrencia: 'a'.repeat(64), titulo: 'Revisar orden', categoria: 'Accion', folio: 'OC-001', modulo: 'compras', motivo: 'Tu firma está pendiente', fechaOcurrencia: '2026-11-11T12:00:00Z', destino: '/compras/ordenes/uno' } }] };
beforeEach(() => { hook.mockReset(); leer.mockClear(); hook.mockReturnValue({ ...base, data }); });
describe('Campana común', () => {
  it('muestra el motivo del último intento de sonido', () => {
    hook.mockReturnValue({ ...base, data, estadoAudio: 'El ERP estaba en segundo plano cuando llegó el aviso.' });
    render(<CampanaAvisos />); fireEvent.click(screen.getByRole('button', { name: /Notificaciones:/ }));
    expect(screen.getByText(/Último estado del sonido:/)).toHaveTextContent('El ERP estaba en segundo plano');
  });
  it('separa sin leer/por hacer, advierte cobertura incompleta y permite paginar', () => {
    render(<CampanaAvisos />); fireEvent.click(screen.getByRole('button', { name: 'Notificaciones: Al menos 32 sin leer' }));
    expect(screen.getByText('Al menos 32 sin leer · Al menos 45 por hacer')).toBeInTheDocument(); expect(screen.getByRole('alert')).toHaveTextContent('No se pudo consultar almacen');
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' })); expect(hook).toHaveBeenLastCalledWith(expect.objectContaining({ continuacion: 'p2' }));
    fireEvent.click(screen.getByRole('button', { name: 'Anterior' })); expect(hook).toHaveBeenLastCalledWith(expect.objectContaining({ continuacion: undefined }));
  });
  it('marcar leído solo solicita lectura y mantiene acción por hacer', () => {
    render(<CampanaAvisos />); fireEvent.click(screen.getByRole('button', { name: /Notificaciones:/ })); fireEvent.click(screen.getByRole('button', { name: 'Marcar leído' })); expect(leer).toHaveBeenCalledWith('a'.repeat(64)); expect(screen.getByText('Por hacer')).toBeInTheDocument();
  });
  it('fallo no muestra falso cero ni contenido anterior', () => {
    hook.mockReturnValue({ ...base, data: undefined, isError: true }); render(<CampanaAvisos />); fireEvent.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(screen.getByRole('alert')).toHaveTextContent('No se pudieron consultar'); expect(screen.queryByText('OC-001')).not.toBeInTheDocument(); expect(screen.queryByText('No hay avisos con estos filtros.')).not.toBeInTheDocument();
  });
  it('rechaza destinos externos y controles', () => {
    expect(destinoInterno('/compras/ordenes/uno')).toBe(true); for (const ruta of ['https://otro', '//otro', '/\\otro', '/\notro']) expect(destinoInterno(ruta)).toBe(false);
  });
});
