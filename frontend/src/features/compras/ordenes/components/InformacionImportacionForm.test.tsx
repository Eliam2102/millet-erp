import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { InformacionImportacionForm } from '@/features/compras/ordenes/components/InformacionImportacionForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoOrdenCompra,
  TipoOperacionComercioExterior,
  TipoTransporte,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/**
 * Sección "Información de importación" + tarjeta "Pedimento y embarque" de
 * una OC de comercio exterior (supuesto Vilo, F1-COM-06). Cubre render con
 * datos, edición del form principal, edición post-autorización del pedimento
 * (PATCH reemplaza los cuatro campos) y error del API visible en toast.
 */

const OC = {
  id: 'oc-1',
  esImportacion: true,
  estado: EstadoOrdenCompra.Borrador,
  infoImportIncotermId: null,
  infoImportPaisOrigen: 'CN',
  infoImportNumeroContenedor: 'ABCU1234567',
  infoImportCodigoRuta: 'RUTA-1',
  infoImportSemanaEmbarque: '2026-W12',
  infoImportNumeroPedimento: '264738135001234',
  infoImportTipoOperacion: TipoOperacionComercioExterior.Importacion,
  infoImportNumeroBl: 'MAEU123456',
  infoImportTipoTransporte: TipoTransporte.Maritimo,
  infoImportAgenteAduanalNaviera: 'Agencia Aduanal Norte',
  infoImportPuertoAduanaEntrada: 'Manzanillo',
  infoImportFechaEstimadaArribo: '2026-11-30',
  infoImportCertificadoOrigen: true,
} as unknown as OrdenCompraDetalleResponse;

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/incoterms', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: ['compras.ordenes.crear', 'compras.ordenes.logistica'],
    errorMessage: null,
  });
});

function renderSeccion(oc: OrdenCompraDetalleResponse = OC) {
  return render(<InformacionImportacionForm oc={oc} />, {
    wrapper: createQueryWrapper(),
  });
}

function boton(container: HTMLElement, action: string): HTMLElement {
  const el = container.querySelector<HTMLElement>(`[data-action="${action}"]`);
  if (!el) throw new Error(`No existe el botón ${action}`);
  return el;
}

describe('<InformacionImportacionForm> — comercio exterior', () => {
  it('muestra los datos capturados en modo lectura, incluidos los campos nuevos', async () => {
    renderSeccion();

    expect(await screen.findByText('Importación')).toBeInTheDocument();
    expect(screen.getByText('Marítimo')).toBeInTheDocument();
    expect(screen.getByText('Manzanillo')).toBeInTheDocument();
    expect(screen.getByText('Sí')).toBeInTheDocument();
    expect(screen.getByText('264738135001234')).toBeInTheDocument();
    expect(screen.getByText('MAEU123456')).toBeInTheDocument();
    expect(screen.getByText('2026-11-30')).toBeInTheDocument();
    expect(screen.getByText('Agencia Aduanal Norte')).toBeInTheDocument();
  });

  it('edita el form principal y envía los campos nuevos al PATCH de importación', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/informacion-importacion', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { container } = renderSeccion();
    fireEvent.click(boton(container, 'editar-info-importacion'));

    fireEvent.change(screen.getByLabelText('Puerto / aduana de entrada'), {
      target: { value: 'Lázaro Cárdenas' },
    });
    fireEvent.click(boton(container, 'submit-info-importacion'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({
      tipoOperacion: TipoOperacionComercioExterior.Importacion,
      tipoTransporte: TipoTransporte.Maritimo,
      numeroBl: 'MAEU123456',
      agenteAduanalNaviera: 'Agencia Aduanal Norte',
      puertoAduanaEntrada: 'Lázaro Cárdenas',
      fechaEstimadaArribo: '2026-11-30',
      certificadoOrigen: true,
    });
    // El pedimento no viaja en el form principal (el backend lo ignora ahí).
    expect(body).not.toHaveProperty('numeroPedimento');
  });

  it('edita el pedimento post-autorización y envía los cuatro campos al PATCH de número de pedimento', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/numero-pedimento', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { container } = renderSeccion();
    fireEvent.click(boton(container, 'editar-numero-pedimento'));

    const tarjeta = container.querySelector<HTMLElement>(
      '[data-component="pedimento-importacion-edit"]',
    );
    if (!tarjeta) throw new Error('No se abrió el form de pedimento');
    fireEvent.change(within(tarjeta).getByLabelText('BL / AWB'), {
      target: { value: 'NUEVOBL01' },
    });
    fireEvent.click(boton(container, 'submit-numero-pedimento'));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toEqual({
      numeroPedimento: '264738135001234',
      numeroBl: 'NUEVOBL01',
      fechaEstimadaArribo: '2026-11-30',
      agenteAduanalNaviera: 'Agencia Aduanal Norte',
    });
  });

  it('muestra el título y el detalle del error del API en un toast y conserva el form', async () => {
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/informacion-importacion', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'IMPORT_PAIS_REQUERIDO',
            detail: 'El país de origen es requerido para importar.',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const errorSpy = vi.spyOn(toast, 'error');

    const { container } = renderSeccion();
    fireEvent.click(boton(container, 'editar-info-importacion'));
    fireEvent.click(boton(container, 'submit-info-importacion'));

    await waitFor(() =>
      expect(errorSpy).toHaveBeenCalledWith(
        'Regla de negocio',
        expect.objectContaining({
          description: expect.stringContaining('El país de origen es requerido para importar.'),
        }),
      ),
    );
    expect(boton(container, 'submit-info-importacion')).toBeInTheDocument();
    errorSpy.mockRestore();
  });
});
