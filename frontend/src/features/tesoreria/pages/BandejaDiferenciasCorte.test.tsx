import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { BandejaDiferenciasCorte } from './BandejaDiferenciasCorte';

let permisos: string[] = [];
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: (p: string) => permisos.includes(p) }));

const fila = (o: object = {}) => ({
  id: 'dif-1', cajaSesionId: 's1', cajaId: 'c1', sucursalId: 'su1', cajeroId: 'cajero-1', diaCorte: '2026-10-28',
  importe: -50, tipo: 'Faltante', estado: 1, motivoRechazo: null, importeDevuelto: 0, pendientePorDevolver: 50,
  resueltaPor: null, resueltaEn: null, recibidoEn: '2026-10-29T12:00:00Z', version: 1, ...o,
});

function bandeja(items: object[]) {
  mswServer.use(
    http.get('*/api/v1/tesoreria/diferencias-corte', () =>
      HttpResponse.json({ items, offset: 0, limit: 100, total: items.length })),
  );
  render(<BandejaDiferenciasCorte />, { wrapper: createQueryWrapper() });
}

describe('<BandejaDiferenciasCorte>', () => {
  beforeEach(() => {
    permisos = ['tesoreria.diferencias-corte.autorizar'];
    useAuthStore.setState({ user: { id: 'tesorero-1' } as never });
  });

  it('lista la diferencia pendiente con Autorizar y Rechazar', async () => {
    bandeja([fila()]);
    expect(await screen.findByText('Pendiente de autorizar')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autorizar' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeEnabled();
  });

  it('el cajero del corte no puede autorizar ni rechazar la suya', async () => {
    useAuthStore.setState({ user: { id: 'cajero-1' } as never });
    bandeja([fila()]);
    expect(await screen.findByRole('button', { name: 'Autorizar' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeDisabled();
  });

  it('sin permiso no ofrece acciones', async () => {
    permisos = [];
    bandeja([fila()]);
    await screen.findByText('Pendiente de autorizar');
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });

  it('autorizar manda la versión esperada y la Idempotency-Key', async () => {
    let visto: Request | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/diferencias-corte/dif-1/autorizar', ({ request }) => {
        visto = request;
        return HttpResponse.json(fila({ estado: 2 }));
      }),
    );
    bandeja([fila()]);
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar' }));
    const confirmar = (await screen.findAllByRole('button', { name: 'Autorizar' })).at(-1)!;
    fireEvent.click(confirmar);
    await waitFor(() => expect(visto).not.toBeNull());
    expect(visto!.headers.get('X-Expected-Version')).toBe('1');
    expect(visto!.headers.get('Idempotency-Key')).toBeTruthy();
  });
});
