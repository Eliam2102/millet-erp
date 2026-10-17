import { afterEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedorClasificacionFiscalSection } from './ProveedorClasificacionFiscalSection';
import type { OrigenProveedor } from '../api/types';

afterEach(() => { cleanup(); useAuthStore.setState({ permisos: [] }); });

function mostrar(origen: OrigenProveedor | null, concepto: string | null, regimen: string | null = '601') {
  useAuthStore.setState({
    status: 'authenticated',
    permisos: [PermisosCanonicos.DatosMaestrosProveedoresGestionar],
    accessToken: 'test-token',
  });
  mswServer.use(
    http.get('*/api/v1/catalogos/regimenes-fiscales', () => HttpResponse.json([])),
  );
  return render(
    <ProveedorClasificacionFiscalSection proveedorId="p-demo" origen={origen} regimenFiscalCodigo={regimen} conceptoRetencion={concepto} />,
    { wrapper: createQueryWrapper() },
  );
}

describe('Clasificación fiscal del proveedor', () => {
  it('muestra «Por revisar» cuando falta origen o concepto', () => {
    mostrar(null, null, null);
    expect(screen.getAllByText('Por revisar').length).toBeGreaterThan(0);
  });

  it('no muestra el badge cuando está completa', () => {
    mostrar(0, 'SIN_RETENCION');
    expect(screen.queryByText('Por revisar')).not.toBeInTheDocument();
  });

  it('deshabilita el régimen en Extranjero y envía el PUT con el body correcto', async () => {
    const enviados: unknown[] = [];
    mswServer.use(http.put('*/api/v1/datos-maestros/proveedores/p-demo/clasificacion-fiscal', async ({ request }) => {
      expect(request.headers.get('Idempotency-Key')).toBeTruthy();
      enviados.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }));
    mostrar(0, 'SIN_RETENCION');
    fireEvent.click(screen.getByRole('button', { name: 'Editar clasificación' }));
    fireEvent.change(screen.getByLabelText('Origen'), { target: { value: '1' } });
    expect(screen.getByRole('combobox', { name: 'Seleccionar régimen fiscal' })).toBeDisabled();
    expect(screen.getByText(/no lleva régimen fiscal/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Guardar clasificación' }));
    await waitFor(() => expect(enviados).toEqual([
      { origen: 1, regimenFiscalCodigo: null, conceptoRetencion: 'SIN_RETENCION' },
    ]));
  });

  it('pega el 422 de régimen al campo', async () => {
    mswServer.use(http.put('*/api/v1/datos-maestros/proveedores/p-demo/clasificacion-fiscal', () =>
      HttpResponse.json(
        { type: 'x', title: 'Régimen requerido', status: 422, code: 'PROVEEDOR_REGIMEN_REQUERIDO', detail: 'Un proveedor nacional requiere régimen fiscal.' },
        { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
      )));
    mostrar(0, 'SIN_RETENCION', null);
    fireEvent.click(screen.getByRole('button', { name: 'Editar clasificación' }));
    fireEvent.click(screen.getByRole('button', { name: 'Guardar clasificación' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Un proveedor nacional requiere régimen fiscal.');
  });
});
