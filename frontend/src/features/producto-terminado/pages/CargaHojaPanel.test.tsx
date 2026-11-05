import { ApiError } from '@/lib/api/error';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CargaHojaPanel } from './CargaHojaPanel';
import type { HojaSalidaDto } from '../api/types';

const carga = vi.hoisted(() => ({ mutateAsync: vi.fn() }));
vi.mock('../api/hooks', () => ({ useCargaHoja: () => ({ mutateAsync: carga.mutateAsync, isPending: false }) }));

const hoja = {
  id: 'h1', folio: 'HS-20261105-A1B2C', sucursalId: 'suc-1', destino: 'Obra Mérida norte', foranea: false, vehiculoId: null, operadorId: null, estado: 'Borrador',
  creadaAt: '2026-11-05T12:00:00Z', emitidaAt: null, cartaPorteFolio: null, pedidos: 2, piezas: 1, yaEmitida: false,
  albaranes: [
    { id: 'a1', folio: 'HS/1', secuencia: 1, numeroPedido: '260001', cliente: 'Constructora Peninsular', destino: 'Torre Altabrisa', emitidoAt: null, total: 1, lineas: [], etiquetas: ['260001-1'], esperadas: 3, disponibles: ['260001-2'], rotas: ['260001-3'] },
    { id: 'a2', folio: 'HS/2', secuencia: 2, numeroPedido: '260002', cliente: 'Desarrollos del Sureste', destino: null, emitidoAt: null, total: 0, lineas: [], etiquetas: [], esperadas: 1, disponibles: ['260002-1'], rotas: [] },
  ],
} as HojaSalidaDto;

beforeEach(() => {
  vi.clearAllMocks();
  carga.mutateAsync.mockResolvedValue(hoja);
});

describe('CargaHojaPanel', () => {
  it('muestra lo cargado, lo disponible y lo roto por pedido, y el avance', () => {
    render(<CargaHojaPanel hoja={hoja} sucursal="Chichí Suárez" emitiendo={false} onEmitir={vi.fn()} />);
    expect(screen.getByText('1 de 4 piezas cargadas')).toBeInTheDocument();
    expect(screen.getByText('Rota · pendiente de reposición')).toBeInTheDocument();
    expect(screen.getByLabelText('Pedido 260002')).toHaveTextContent('0 de 1 cargadas');
  });

  it('escanear manda la etiqueta, limpia la entrada y avisa; Enter envía', async () => {
    render(<CargaHojaPanel hoja={hoja} sucursal="Chichí Suárez" emitiendo={false} onEmitir={vi.fn()} />);
    const entrada = screen.getByLabelText('Etiqueta de la pieza');
    fireEvent.change(entrada, { target: { value: ' 260001-2 ' } });
    fireEvent.submit(entrada.closest('form')!);
    await waitFor(() => expect(carga.mutateAsync).toHaveBeenCalledWith({ id: 'h1', accion: 'cargar', etiqueta: '260001-2' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Cargada 260001-2.');
    expect(entrada).toHaveValue('');
  });

  it('una pieza rechazada deja el aviso de error y conserva lo escaneado', async () => {
    carga.mutateAsync.mockRejectedValueOnce(new ApiError({ type: 'about:blank', status: 422, title: 'Pieza de otro pedido', detail: 'La pieza 260009-1 es del pedido 260009, que no está en esta ruta.' }, 422));
    render(<CargaHojaPanel hoja={hoja} sucursal="Chichí Suárez" emitiendo={false} onEmitir={vi.fn()} />);
    const entrada = screen.getByLabelText('Etiqueta de la pieza');
    fireEvent.change(entrada, { target: { value: '260009-1' } });
    fireEvent.submit(entrada.closest('form')!);
    expect(await screen.findByRole('alert')).toHaveTextContent('no está en esta ruta');
    expect(entrada).toHaveValue('260009-1');
  });

  it('quitar, marcar rota y emitir', async () => {
    const onEmitir = vi.fn();
    render(<CargaHojaPanel hoja={hoja} sucursal="Chichí Suárez" emitiendo={false} onEmitir={onEmitir} />);
    fireEvent.click(screen.getByRole('button', { name: 'Quitar' }));
    await waitFor(() => expect(carga.mutateAsync).toHaveBeenCalledWith({ id: 'h1', accion: 'descargar', etiqueta: '260001-1' }));
    fireEvent.click(within260002Rota());
    await waitFor(() => expect(carga.mutateAsync).toHaveBeenCalledWith({ id: 'h1', accion: 'rota', etiqueta: '260002-1' }));
    fireEvent.click(screen.getByRole('button', { name: 'Emitir lo cargado' }));
    expect(onEmitir).toHaveBeenCalled();
  });

  it('sin nada cargado no se puede emitir', () => {
    render(<CargaHojaPanel hoja={{ ...hoja, albaranes: hoja.albaranes.map((a) => ({ ...a, total: 0, etiquetas: [] })) }} sucursal="Chichí Suárez" emitiendo={false} onEmitir={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Emitir lo cargado' })).toBeDisabled();
  });
});

function within260002Rota() {
  const fila = screen.getByText('260002-1').closest('li')!;
  return Array.from(fila.querySelectorAll('button')).find((b) => b.textContent === 'Rota')!;
}
