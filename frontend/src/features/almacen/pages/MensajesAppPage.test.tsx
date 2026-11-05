import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { MensajesAppPage } from '@/features/almacen/pages/MensajesAppPage';
import type { MensajeAppMarcado } from '@/features/almacen/api/useConsumoComponentes';

const hook = vi.hoisted(() => ({ useMensajesAppMarcados: vi.fn() }));
vi.mock('@/features/almacen/api/useConsumoComponentes', () => ({ useMensajesAppMarcados: hook.useMensajesAppMarcados }));

const sinExistencia: MensajeAppMarcado = {
  mensajeId: 'MSG-0001',
  tipo: 'SALIDA_MANUAL',
  estado: 'EN_REVISION',
  referencia: 'SAL-1',
  folio: 'REV-MSG-0001',
  error: 'Sin existencia suficiente (artículo 100006 3302x6100): disponible 0, solicitada 20.1422.',
  recibidoAt: '2026-11-05T15:00:00Z',
  payload: JSON.stringify({ datos: { movimiento: 'SM-000123' } }),
};

beforeEach(() => vi.clearAllMocks());

describe('MensajesAppPage', () => {
  it('lista la salida sin existencia con su folio de revisión, el movimiento de la app y el motivo', () => {
    hook.useMensajesAppMarcados.mockReturnValue({ data: [sinExistencia], isLoading: false, isError: false, refetch: vi.fn() });
    render(<MensajesAppPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('REV-MSG-0001')).toBeInTheDocument();
    expect(screen.getByText('SM-000123')).toBeInTheDocument();
    expect(screen.getByText(/Sin existencia suficiente \(artículo 100006/)).toBeInTheDocument();
  });

  it('sin mensajes marcados muestra el estado vacío', () => {
    hook.useMensajesAppMarcados.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });
    render(<MensajesAppPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Nada marcado')).toBeInTheDocument();
  });
});
