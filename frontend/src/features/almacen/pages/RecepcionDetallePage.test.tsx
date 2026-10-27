import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RecepcionDetallePage } from '@/features/almacen/pages/RecepcionDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoMovimiento } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'r-1' }),
  useSearch: () => ({}),
}));

const SUB_ID = '00000008-0001-0000-0000-000000000004';
const OC_ID = '019e5ba8-41b2-7ee8-8f40-4435f4808968';
const ART_ID = '00000005-0002-0000-0000-000000000001';

const detalleConNombres = {
  id: 'r-1',
  folio: 'M-ENT2026-000005',
  fechaMovimiento: '2026-06-01',
  subAlmacenId: SUB_ID,
  subAlmacenClave: 'ALM-CEN',
  subAlmacenNombre: 'Almacén Central',
  ordenCompraId: OC_ID,
  ordenCompraFolio: 'OC-MID2026-000050',
  cfdiRecibidoId: null,
  facturaId: null,
  cfdiUuidFiscal: null,
  facturaFolio: null,
  estado: EstadoMovimiento.Registrado,
  version: 1,
  observaciones: null,
  registradoAt: null,
  registradoPor: null,
  lineas: [
    {
      id: 'l-1',
      posicion: 1,
      articuloId: ART_ID,
      articuloClave: 'ACC-001',
      articuloDescripcion: 'Tornillo M6',
      cantidad: 2,
      unidadMedida: 'PZA',
      costoUnitarioMxn: 10,
      montoTotalMxn: 20,
    },
  ],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.entradas.leer'],
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

describe('<RecepcionDetallePage> — nombres resueltos', () => {
  it('muestra nombres (sub-almacén, folio OC, artículo) y NO los ids crudos', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () => HttpResponse.json(detalleConNombres)),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('ALM-CEN · Almacén Central')).toBeInTheDocument());
    expect(screen.getByText('OC-MID2026-000050')).toBeInTheDocument();
    expect(screen.getByText('Tornillo M6')).toBeInTheDocument();

    // Con nombre/folio disponible, ningún id crudo se pinta.
    expect(screen.queryByText(SUB_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(OC_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(ART_ID)).not.toBeInTheDocument();
  });

  it('F1-COM-06 muestra pedimento, BL, contenedor, aduana, clave y fracción de la partida', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          importacion: {
            numeroPedimento: '261612346000001',
            numeroBl: 'BL-DEMO',
            numeroContenedor: 'CONT-DEMO',
            aduana: '16',
            aduanaNombre: 'Manzanillo',
            clavePedimento: 'A1',
            clavePedimentoNombre: 'Importación definitiva',
          },
          lineas: [{ ...detalleConNombres.lineas[0], fraccionArancelaria: '84719000' }],
        }),
      ),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('261612346000001')).toBeInTheDocument());
    expect(screen.getByText('BL-DEMO')).toBeInTheDocument();
    expect(screen.getByText('CONT-DEMO')).toBeInTheDocument();
    expect(screen.getByText('16 · Manzanillo')).toBeInTheDocument();
    expect(screen.getByText('A1 · Importación definitiva')).toBeInTheDocument();
    expect(screen.getByText('84719000')).toBeInTheDocument();
  });

  it('sin importación no pinta la columna de fracción', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () => HttpResponse.json(detalleConNombres)),
    );
    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Tornillo M6')).toBeInTheDocument());
    expect(screen.queryByText('Fracción arancelaria')).not.toBeInTheDocument();
  });

  it('marca las líneas en revisión con su motivo y avisa que no están disponibles', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          lineas: [
            detalleConNombres.lineas[0],
            {
              ...detalleConNombres.lineas[0],
              id: 'l-2',
              posicion: 2,
              enRevision: true,
              motivoDano: 'Hojas rotas',
            },
          ],
        }),
      ),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('En revisión')).toBeInTheDocument());
    expect(screen.getByText('Hojas rotas')).toBeInTheDocument();
    expect(
      screen.getByText(/1 partida en material en revisión: no están disponibles hasta el dictamen/),
    ).toBeInTheDocument();
  });

  it('cae a los ids crudos cuando el backend no resuelve nombres', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          subAlmacenClave: null,
          subAlmacenNombre: null,
          ordenCompraFolio: null,
          lineas: [
            {
              ...detalleConNombres.lineas[0],
              articuloClave: null,
              articuloDescripcion: null,
            },
          ],
        }),
      ),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText(SUB_ID)).toBeInTheDocument());
    expect(screen.getByText(OC_ID)).toBeInTheDocument();
    expect(screen.getByText(ART_ID)).toBeInTheDocument();
  });

  it('muestra UUID fiscal del CFDI y folio de factura; cae al id truncado si no resuelven', async () => {
    const CFDI_ID = '019e0000-0000-7000-8000-000000000001';
    const FACT_ID = '019e0000-0000-7000-8000-000000000002';
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({
          ...detalleConNombres,
          cfdiRecibidoId: CFDI_ID,
          cfdiUuidFiscal: '5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B',
          facturaId: FACT_ID,
          facturaFolio: null, // el puerto no resolvió la factura
        }),
      ),
    );

    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });

    // CFDI resuelto: se pinta el folio fiscal del SAT, no el GUID interno.
    await waitFor(() =>
      expect(screen.getByText('5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B')).toBeInTheDocument(),
    );
    expect(screen.queryByText(CFDI_ID)).not.toBeInTheDocument();
    // Factura sin resolver: id truncado, nunca el GUID completo.
    expect(screen.getByText('019e0000…')).toBeInTheDocument();
    expect(screen.queryByText(FACT_ID)).not.toBeInTheDocument();
  });
});

describe('<RecepcionDetallePage> — cancelar recepción', () => {
  const PERMISO = 'almacen.recepciones.cancelar';
  const conPermiso = () =>
    useAuthStore.setState({ permisos: ['almacen.entradas.leer', PERMISO] });
  const servir = (extra: object) =>
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones/r-1', () =>
        HttpResponse.json({ ...detalleConNombres, registradaPorOtroUsuario: true, ...extra }),
      ),
    );
  const boton = () => screen.queryByRole('button', { name: 'Cancelar recepción' });

  it('muestra el botón con permiso, Registrado y registrada por otro usuario', async () => {
    conPermiso();
    servir({});
    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(boton()).toBeInTheDocument());
  });

  it('lo oculta sin permiso', async () => {
    servir({});
    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });
    await screen.findByText('Tornillo M6');
    expect(boton()).not.toBeInTheDocument();
  });

  it('lo oculta si ya está Cancelado y muestra la bitácora', async () => {
    conPermiso();
    servir({
      estado: EstadoMovimiento.Cancelado,
      registradoPorNombre: 'Ana A',
      canceladaAt: '2026-06-02T10:00:00Z',
      canceladaPorNombre: 'Beto B',
      motivoCancelacion: 'Artículo mal capturado',
      foliosCancelacion: ['M-CEN2026-000009'],
    });
    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });
    await screen.findByText('Bitácora');
    expect(screen.getByText('Beto B')).toBeInTheDocument();
    expect(screen.getByText('Artículo mal capturado')).toBeInTheDocument();
    expect(screen.getByText('M-CEN2026-000009')).toBeInTheDocument();
    expect(boton()).not.toBeInTheDocument();
  });

  it('lo oculta si la registró el mismo usuario', async () => {
    conPermiso();
    servir({ registradaPorOtroUsuario: false });
    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });
    await screen.findByText('Tornillo M6');
    expect(boton()).not.toBeInTheDocument();
  });

  it('el diálogo exige motivo y texto antes de confirmar', async () => {
    conPermiso();
    servir({});
    render(<RecepcionDetallePage />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Cancelar recepción' }));
    const dialogo = await screen.findByRole('dialog');
    const confirmar = dialogo.querySelector('[data-action="confirmar-cancelacion"]');
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Explica qué se capturó mal/), {
      target: { value: 'Se capturó otro artículo' },
    });
    // Falta el código de motivo: sigue deshabilitado.
    expect(confirmar).toBeDisabled();
  });
});
