import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AvisoCsdBanner } from '@/features/integraciones-fiscal/components/AvisoCsdBanner';

vi.mock('@/lib/auth/useHasPermission', () => ({
  useHasPermission: () => true,
  useHasAnyPermission: () => true,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

function estado(datos: Record<string, unknown>) {
  mswServer.use(http.get('*/api/v1/integraciones/fiscal/csd/estado', () => HttpResponse.json(datos)));
}

describe('<AvisoCsdBanner> (F6.9)', () => {
  it('avisa los días que faltan cuando el CSD está por vencer', async () => {
    estado({ estado: 'ProximoAVencer', numeroSerie: '30001000000500003416', venceEn: '2026-11-10T18:00:00Z', diasRestantes: 10, bloqueaTimbrado: false });
    render(<AvisoCsdBanner />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('alert')).toHaveTextContent(/vence el .* \(10 días\)/);
    expect(screen.getByRole('link', { name: 'Ver certificados' })).toHaveAttribute('href', '/admin/integraciones/fiscal');
  });

  it('avisa que no se puede timbrar con un CSD revocado', async () => {
    estado({ estado: 'Revocado', numeroSerie: '30001000000500003416', venceEn: '2027-05-01T00:00:00Z', diasRestantes: 180, bloqueaTimbrado: true });
    render(<AvisoCsdBanner />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('alert')).toHaveTextContent('está revocado: no se puede timbrar');
  });

  it('no muestra nada con un CSD vigente', async () => {
    estado({ estado: 'Vigente', numeroSerie: 'x', venceEn: '2028-01-01T00:00:00Z', diasRestantes: 400, bloqueaTimbrado: false });
    const { container } = render(<AvisoCsdBanner />, { wrapper: createQueryWrapper() });
    await new Promise((r) => setTimeout(r, 50));
    expect(container).toBeEmptyDOMElement();
  });
});
