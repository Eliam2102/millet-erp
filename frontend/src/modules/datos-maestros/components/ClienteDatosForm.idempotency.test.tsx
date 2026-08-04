import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteDatosForm } from '@/modules/datos-maestros/components/ClienteDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';

/**
 * Regresión — Idempotency-Key por submit (ADR-0020, Bug B). Espejo del
 * test de proveedor: el form de edición de cliente es multi-submit;
 * una key estable por montaje daría 422 al 2º guardado con un body
 * distinto. Se genera una key fresca por submit.
 *
 * <para>De paso verifica el mapping <c>limpiarX</c>: RFC vacío viaja
 * como <c>rfc: null + limpiarRfc: true</c>.</para>
 */

const CLIENTE: ClienteDetalle = {
  id: 'c-1',
  clave: 'CLI-001',
  referenciaExterna: null,
  razonSocial: 'Cliente Uno SA',
  rfc: 'CUN010101AAA',
  regimenFiscal: '601',
  codigoPostalFiscal: '76100',
  usoCfdiDefault: null,
  formaPagoDefault: null,
  metodoPagoDefault: null,
  monedaDefault: 'MXN',
  esGenerico: false,
  origen: 0,
  email: null,
  telefono: null,
  datosFiscalesCompletos: true,
  estatus: 0,
};

beforeEach(() => {
  // Los selectores de catálogo (moneda, régimen, uso CFDI, forma de
  // pago) disparan su query al montar; lista vacía basta.
  mswServer.use(
    http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/regimenes-fiscales', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/catalogos/usos-cfdi', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosClientesGestionar],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<ClienteDatosForm> — Idempotency-Key por submit (regresión Bug B)', () => {
  it('dos guardados del mismo cliente envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.patch(
        '*/api/v1/datos-maestros/clientes/c-1',
        async ({ request }) => {
          keys.push(request.headers.get('Idempotency-Key'));
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    render(<ClienteDatosForm cliente={CLIENTE} />, {
      wrapper: createQueryWrapper(),
    });

    const razon = screen.getByDisplayValue('Cliente Uno SA') as HTMLInputElement;
    const guardar = () =>
      screen.getByRole('button', { name: /guardar cambios/i });

    // Guardado #1.
    fireEvent.change(razon, { target: { value: 'Cliente Uno SA de CV' } });
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(1));

    // 2º cambio con body distinto (el form quedó montado).
    fireEvent.change(razon, { target: { value: 'Cliente Uno SAPI' } });
    await waitFor(() => expect(guardar()).toBeEnabled());
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });

  it('RFC vaciado viaja como rfc: null + limpiarRfc: true', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch(
        '*/api/v1/datos-maestros/clientes/c-1',
        async ({ request }) => {
          body = (await request.json()) as Record<string, unknown>;
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    render(<ClienteDatosForm cliente={CLIENTE} />, {
      wrapper: createQueryWrapper(),
    });

    const rfc = screen.getByDisplayValue('CUN010101AAA') as HTMLInputElement;
    fireEvent.change(rfc, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({
      rfc: null,
      limpiarRfc: true,
      // Los que siguen con valor NO se limpian.
      regimenFiscal: '601',
      limpiarRegimenFiscal: false,
      codigoPostalFiscal: '76100',
      limpiarCodigoPostalFiscal: false,
    });
  });
});
