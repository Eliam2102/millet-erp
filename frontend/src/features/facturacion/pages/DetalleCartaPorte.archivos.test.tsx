import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleCartaPorte } from '@/features/facturacion/pages/DetalleCartaPorte';

const descargarArchivo = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));
vi.mock('@/features/facturacion/lib/descargas', () => ({ descargarArchivo }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'c-1' }),
  useSearch: () => ({}),
  useNavigate: () => () => {},
}));

function detalle(estado: string) {
  return {
    id: 'c-1',
    folio: 'CP-1',
    estado,
    uuid: estado === 'Timbrado' ? 'U-1' : null,
    tipo: 'T',
    origen: 'Cancún',
    destino: 'Mérida',
    distanciaKm: 300,
    fechaSalida: '2026-05-30T08:00:00Z',
    fechaLlegadaEstimada: '2026-05-30T14:00:00Z',
    cartaPortePreviaId: null,
    total: 0,
    vehiculo: { id: 'v-1', placa: 'ABC123', configVehicular: 'C2', anioModelo: 2020 },
    operador: { id: 'o-1', rfc: 'XXXX010101XX1', nombre: 'Operador Demo', numLicencia: 'L-9' },
    mercancias: [],
  };
}

describe('<DetalleCartaPorte> — PDF y XML (86e3tfdx0)', () => {
  it('descarga el PDF con el formato de Millet y el XML timbrado', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte/c-1', () => HttpResponse.json(detalle('Timbrado'))),
    );
    render(<DetalleCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('CP-1')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /PDF/ }));
    fireEvent.click(screen.getByRole('button', { name: /XML/ }));

    await waitFor(() =>
      expect(descargarArchivo).toHaveBeenCalledWith('/api/v1/facturacion/carta-porte/c-1/pdf', 'CP-1.pdf'),
    );
    expect(descargarArchivo).toHaveBeenCalledWith('/api/v1/facturacion/carta-porte/c-1/xml', 'CP-1.xml');
  });

  it('sin timbrar: el PDF sale como vista previa y el XML queda deshabilitado', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte/c-1', () => HttpResponse.json(detalle('Borrador'))),
    );
    render(<DetalleCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('CP-1')).toBeInTheDocument());

    expect(screen.getByRole('button', { name: /XML/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /PDF/ })).toBeEnabled();
  });
});
