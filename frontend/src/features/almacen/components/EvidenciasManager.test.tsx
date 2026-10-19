import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { EvidenciasManager } from '@/features/almacen/components/EvidenciasManager';
import type { DevolucionProveedorEvidenciaItem } from '@/features/almacen/api/types';

const api = vi.hoisted(() => ({ apiRequest: vi.fn() }));

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/almacen/components/EvidenciaUpload', () => ({ EvidenciaUpload: () => null }));
vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  apiRequest: api.apiRequest,
}));

const evidencia = (blobRef: string): DevolucionProveedorEvidenciaItem => ({
  id: 'e-1',
  tipoEvidencia: 'Foto del defecto',
  nombreArchivo: 'foto.png',
  blobRef,
  comentario: null,
});

beforeEach(() => vi.clearAllMocks());

describe('EvidenciasManager', () => {
  it('evidencia adjunto:{id} descarga por el endpoint genérico de adjuntos de la línea de revisión', async () => {
    api.apiRequest.mockResolvedValue({ data: { url: '/dl/abc', expiraEn: 'x' } });
    render(
      <EvidenciasManager
        devolucionId="d-1"
        evidencias={[evidencia('adjunto:11111111-1111-1111-1111-111111111111')]}
        canAdd={false}
        lineaRevisionId="lr-1"
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByText(/adjunto:/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Descargar' }));
    await waitFor(() => expect(api.apiRequest).toHaveBeenCalled());
    expect(api.apiRequest).toHaveBeenCalledWith(
      '/api/v1/almacen/revision/lineas/lr-1/adjuntos/11111111-1111-1111-1111-111111111111/enlace',
      { method: 'POST' },
    );
  });

  it('evidencia con blob normal sigue mostrando la referencia y no ofrece descarga', () => {
    render(
      <EvidenciasManager devolucionId="d-1" evidencias={[evidencia('evidencias/foto.png')]} canAdd={false} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('evidencias/foto.png')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Descargar' })).not.toBeInTheDocument();
  });
});
