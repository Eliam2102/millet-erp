import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConsumosRevisionPage } from '@/features/almacen/pages/ConsumosRevisionPage';
import type { ConsumoEnRevision } from '@/features/almacen/api/useConsumoComponentes';

const hook = vi.hoisted(() => ({ useConsumosEnRevision: vi.fn() }));
vi.mock('@/features/almacen/api/useConsumoComponentes', () => ({ useConsumosEnRevision: hook.useConsumosEnRevision }));
vi.mock('@tanstack/react-router', () => ({ Link: ({ children }: { children: React.ReactNode }) => <a href="/">{children}</a> }));

const sinNorma: ConsumoEnRevision = {
  mensajeId: 'DEMO:E1:999970:530000',
  tipo: 'CONSUMO_COMPONENTE',
  estado: 'EN_REVISION',
  referencia: 'Pedido 7000123 pos 1',
  error: 'Sin norma de consumo para el artículo A+W 530000.',
  recibidoAt: '2026-11-05T15:00:00Z',
  payload: JSON.stringify({ etiqueta: 'E1', operacion: 999970, componente: '530000', capas: 1, anchoMm: 875.51, altoMm: 1041.65, sucursal: 'CIRCUITO' }),
};

beforeEach(() => vi.clearAllMocks());

describe('ConsumosRevisionPage', () => {
  it('lista el consumo sin norma con su componente, medidas y motivo', () => {
    hook.useConsumosEnRevision.mockReturnValue({ data: [sinNorma], isLoading: false, isError: false, refetch: vi.fn() });
    render(<ConsumosRevisionPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('530000')).toBeInTheDocument();
    expect(screen.getByText('Ensamblado')).toBeInTheDocument();
    expect(screen.getByText('875.51 × 1,041.65')).toBeInTheDocument();
    expect(screen.getByText('Sin norma de consumo para el artículo A+W 530000.')).toBeInTheDocument();
  });

  it('sin consumos pendientes muestra el estado vacío', () => {
    hook.useConsumosEnRevision.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });
    render(<ConsumosRevisionPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Nada en revisión')).toBeInTheDocument();
  });
});
