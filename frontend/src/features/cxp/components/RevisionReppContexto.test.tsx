import { render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { RevisionReppContexto } from './RevisionReppContexto';
const permisos = vi.hoisted(() => ({ tesoreria: true }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permisos.tesoreria }));
vi.mock('@tanstack/react-router', () => ({ Link: ({ to, children }: { to: string; children: React.ReactNode }) => <a href={to}>{children}</a> }));
const contexto = {
  pendiente: true,
  ultimaAutorizacionEn: '2026-11-03T23:10:56Z',
  pagos: [{ pagoId: 'pago-ficticio', fechaPago: '2026-11-03', importe: 8700, cubiertoRepp: 0, moneda: 'MXN' }],
};
beforeEach(() => { permisos.tesoreria = true; });
it('explica revisión posterior al pago y enlaza la pantalla real sin registrar nada', () => {
  render(<RevisionReppContexto contexto={contexto} />);
  expect(screen.getByText(/autorización anterior/i)).toBeInTheDocument();
  expect(screen.getByText('03/11/2026')).toBeInTheDocument();
  expect(screen.getByText(/8,700.00/)).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /REPP de proveedor/i })).toHaveAttribute('href', '/tesoreria/repp');
});
it('sin permiso mantiene explicación y remite al área sin ofrecer acceso', () => {
  permisos.tesoreria = false;
  render(<RevisionReppContexto contexto={contexto} />);
  expect(screen.queryByRole('link')).not.toBeInTheDocument();
  expect(screen.getByText(/Solicita a Tesorería/i)).toBeInTheDocument();
});
it('no inventa autorización ni pago cuando no existen datos', () => {
  render(<RevisionReppContexto contexto={{ ...contexto, ultimaAutorizacionEn: null, pagos: [] }} />);
  expect(screen.queryByText(/autorización anterior/i)).not.toBeInTheDocument();
  expect(screen.getByText(/sin pagos identificados/i)).toBeInTheDocument();
});
it('otro motivo o complemento cubierto no muestra bloqueo de REPP', () => {
  const { rerender } = render(<RevisionReppContexto contexto={null} />);
  expect(screen.queryByText(/complemento de pago/i)).not.toBeInTheDocument();
  rerender(<RevisionReppContexto contexto={{ ...contexto, pendiente: false }} />);
  expect(screen.queryByText(/complemento de pago/i)).not.toBeInTheDocument();
});
