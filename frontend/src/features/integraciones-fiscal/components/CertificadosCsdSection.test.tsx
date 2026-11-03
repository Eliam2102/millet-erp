import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CertificadosCsdSection } from '@/features/integraciones-fiscal/components/CertificadosCsdSection';
import { ProveedorPac } from '@/features/integraciones-fiscal/api/types';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

const base = '*/api/v1/integraciones/fiscal/configuracion/e-1/1';
const vigente = {
  id: 'c-2',
  numeroSerie: '30001000000500003417',
  rfc: 'EKU9003173C9',
  vigenteDesde: '2026-01-01T00:00:00Z',
  venceEn: '2026-11-10T00:00:00Z',
  estado: 'Vigente',
  registradoEn: '2026-10-01T00:00:00Z',
  registradoPor: 'Admin fiscal',
  finalizadoEn: null,
  finalizadoPor: null,
  motivoRevocacion: null,
  alertasEnviadas: [30, 15],
};
const sustituido = { ...vigente, id: 'c-1', numeroSerie: '30001000000500003416', estado: 'Sustituido', finalizadoEn: '2026-10-01T00:00:00Z', finalizadoPor: 'Admin fiscal', alertasEnviadas: [] };

describe('<CertificadosCsdSection> (F6.9)', () => {
  it('muestra el historial con avisos enviados y revoca con motivo', async () => {
    let motivo: string | null = null;
    mswServer.use(
      http.get(`${base}/certificados`, () => HttpResponse.json([vigente, sustituido])),
      http.post(`${base}/csd/revocar`, async ({ request }) => {
        motivo = ((await request.json()) as { motivo: string }).motivo;
        return HttpResponse.json({ ...vigente, estado: 'Revocado', motivoRevocacion: motivo });
      }),
    );
    render(<CertificadosCsdSection empresaId="e-1" proveedor={ProveedorPac.FiscalApi} />, {
      wrapper: createQueryWrapper(),
    });

    expect(await screen.findByText('30001000000500003417')).toBeInTheDocument();
    expect(screen.getByText('30 días, 15 días')).toBeInTheDocument();
    expect(screen.getByText('Sustituido')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Revocar CSD en uso' }));
    fireEvent.click(screen.getByRole('button', { name: 'Revocar' }));
    expect(motivo).toBeNull();

    fireEvent.change(screen.getByLabelText('Motivo de la revocación *'), { target: { value: 'Llave comprometida' } });
    fireEvent.click(screen.getByRole('button', { name: 'Revocar' }));
    await waitFor(() => expect(motivo).toBe('Llave comprometida'));
  });
});
