import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AdjuntosManagerOc } from './AdjuntosManagerOc';
import { useAuthStore } from '@/lib/auth/auth-store';
import type {
  TipoDocumentoSelectorItem,
  TipoDocumentoSelectorProps,
} from '@/components/erp/adjuntos/TipoDocumentoSelector';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
  type OrdenCompraDetalleResponse,
} from '../api/types';

vi.mock('@/components/erp/adjuntos/TipoDocumentoSelector', () => ({
  TipoDocumentoSelector: ({
    items,
    value,
    onChange,
    disabled,
    ariaLabel = 'Tipo de documento',
  }: TipoDocumentoSelectorProps<TipoDocumentoSelectorItem>) => (
    <select
      aria-label={ariaLabel}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
      disabled={disabled}
      data-component="tipo-documento-selector"
    >
      <option value="">Selecciona tipo</option>
      {items.map((i) => (
        <option key={i.id} value={i.id}>
          {i.descripcion}
        </option>
      ))}
    </select>
  ),
}));

describe('<AdjuntosManagerOc> — pruebas de flujo y rechazos (F1-ADM-11, D3)', () => {
  const ocId = '00000000-0000-0000-0000-000000000001';
  const tipoCotizacionId = '00000003-0003-0010-0000-000000000001';

  // Solo los campos que usa AdjuntosManagerOc; mismo patrón parcial que EditorLineas.cc.test.tsx.
  const ocBase = {
    id: ocId,
    empresaId: '00000003-0000-0000-0000-000000000001',
    folio: 'OC-2026-000001',
    folioAnio: 2026,
    proveedorId: '00000005-0001-0000-0000-000000000001',
    proveedorRazonSocial: 'Vidrios y Cristales SA',
    proveedorClave: 'PROV-001',
    sucursalDestinoId: '00000001-0001-0000-0000-000000000001',
    condicionesPagoId: '00000001-0002-0000-0000-000000000001',
    usoPrincipalId: '00000001-0003-0000-0000-000000000001',
    moneda: 'MXN',
    tipoCambio: null,
    compradorTitularId: 'u-1',
    encargadoComprasId: 'u-1',
    observaciones: null,
    sinRequisicionPrevia: false,
    esImportacion: false,
    cotizacionExcepcionada: false,
    fechaDocumento: '2026-05-09',
    fechaContabilizacion: null,
    fechaEntregaEsperada: null,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    motivoSinRequisicion: null,
    motivoCancelacion: null,
    motivoRechazoId: null,
    motivoRechazoTexto: null,
    ocOrigenId: null,
    version: 1,
    lineas: [],
    adjuntos: [],
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
  } as unknown as OrdenCompraDetalleResponse;

  beforeAll(() => {
    URL.createObjectURL = vi.fn(() => 'blob:mock-object-url');
    URL.revokeObjectURL = vi.fn();
    window.HTMLElement.prototype.hasPointerCapture = () => false;
    window.HTMLElement.prototype.setPointerCapture = () => {};
    window.HTMLElement.prototype.releasePointerCapture = () => {};
  });

  beforeEach(() => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'user@millet.com', nombre: 'Usuario Compras' },
      empresas: [],
      currentEmpresaId: '00000003-0000-0000-0000-000000000001',
      permisos: [
        'compras.ordenes.leer',
        'compras.ordenes.adjuntar',
        'compras.ordenes.crear',
      ],
      errorMessage: null,
    });

    mswServer.use(
      http.get('*/api/v1/compras/catalogos/tipos-documento-oc', () =>
        HttpResponse.json([
          {
            id: tipoCotizacionId,
            clave: 'COT',
            descripcion: 'Cotización',
            obligatorioSiImportacion: false,
            activo: true,
          },
        ]),
      ),
      http.get('*/api/v1/identidad/usuarios', () =>
        HttpResponse.json({
          items: [
            { id: 'u-1', email: 'user@millet.com', nombre: 'Usuario Compras' },
          ],
          total: 1,
        }),
      ),
      http.get('*/api/v1/compras/ordenes/:ocId/adjuntos/:adjuntoId/contenido', () =>
        new HttpResponse(new Uint8Array([0x25, 0x50, 0x44, 0x46]), {
          headers: { 'Content-Type': 'application/pdf' },
        }),
      ),
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
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

  it('archivo con formato o tamaño inválido no invoca subida y muestra mensaje de validación', async () => {
    let uploadInvocado = false;
    mswServer.use(
      http.post('*/api/v1/compras/ordenes/:ocId/adjuntos', () => {
        uploadInvocado = true;
        return HttpResponse.json({ adjuntoId: 'adj-new' }, { status: 201 });
      }),
    );

    const { container } = render(<AdjuntosManagerOc oc={ocBase} />, {
      wrapper: createQueryWrapper(),
    });

    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
    expect(fileInput).not.toBeNull();

    // 1. Probar archivo con extensión / tipo MIME no permitido (.exe)
    const archivoInvalido = new File(['MZ executable content'], 'programa.exe', {
      type: 'application/x-msdownload',
    });
    fireEvent.change(fileInput, { target: { files: [archivoInvalido] } });

    const errorFormato = await screen.findByRole('alert');
    expect(errorFormato).toHaveTextContent(/tipo de archivo no permitido/i);
    expect(uploadInvocado).toBe(false);
    expect(container.querySelector('[data-component="adjuntos-staging"]')).toBeNull();

    // 2. Probar archivo que excede los 20 MB (25 MB)
    const archivoPesado = new File([''], 'grande.pdf', {
      type: 'application/pdf',
    });
    Object.defineProperty(archivoPesado, 'size', { value: 25 * 1024 * 1024 });

    fireEvent.change(fileInput, { target: { files: [archivoPesado] } });

    const errorTamano = await screen.findByRole('alert');
    expect(errorTamano).toHaveTextContent(/archivo demasiado grande/i);
    expect(uploadInvocado).toBe(false);
    expect(container.querySelector('[data-component="adjuntos-staging"]')).toBeNull();
  });

  it('cuando el backend rechaza la subida (422 o 403), muestra el mensaje del servidor y la lista no cambia', async () => {
    const toastErrorSpy = vi.spyOn(toast, 'error');

    mswServer.use(
      http.post('*/api/v1/compras/ordenes/:ocId/adjuntos', () =>
        HttpResponse.json(
          {
            type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
            title: 'El formato o extensión del archivo no está permitido (F1-ADM-11).',
            status: 422,
            code: 'ADJUNTO_FORMATO_NO_PERMITIDO',
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { container } = render(<AdjuntosManagerOc oc={ocBase} />, {
      wrapper: createQueryWrapper(),
    });

    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement;
    const archivoPdf = new File(['%PDF-1.4 dummy'], 'cotizacion.pdf', {
      type: 'application/pdf',
    });
    fireEvent.change(fileInput, { target: { files: [archivoPdf] } });

    // Staging debe aparecer
    await waitFor(() =>
      expect(container.querySelector('[data-component="adjuntos-staging"]')).not.toBeNull(),
    );

    // Seleccionar tipo de documento
    const selector = await screen.findByRole('combobox', { name: /tipo de documento/i });
    fireEvent.change(selector, { target: { value: tipoCotizacionId } });

    // Confirmar subida
    const botonSubir = screen.getByRole('button', { name: /^subir$/i });
    fireEvent.click(botonSubir);

    // Verificar que el mensaje del servidor se muestra tal cual
    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'El formato o extensión del archivo no está permitido (F1-ADM-11).',
      );
    });

    // Toast recibe el mensaje del servidor como descripción
    expect(toastErrorSpy).toHaveBeenCalledWith(
      'No se pudo subir el archivo.',
      expect.objectContaining({
        description: 'El formato o extensión del archivo no está permitido (F1-ADM-11).',
      }),
    );

    // La lista de adjuntos sigue vacía
    expect(screen.getByText('Sin adjuntos.')).toBeInTheDocument();
  });

  it('remount con el mismo oc.adjuntos mantiene las filas y resuelve la URL autenticada de contenido', async () => {
    const ocConAdjunto: OrdenCompraDetalleResponse = {
      ...ocBase,
      adjuntos: [
        {
          id: 'adj-100',
          tipoDocumentoId: tipoCotizacionId,
          nombreArchivo: 'presupuesto_proveedor.pdf',
          contentType: 'application/pdf',
          tamanoBytes: 54321,
          fechaCarga: '2026-06-10T12:00:00Z',
          usuarioCargaId: 'u-1',
        },
      ],
    };

    const { unmount } = render(<AdjuntosManagerOc oc={ocConAdjunto} />, {
      wrapper: createQueryWrapper(),
    });

    // Primera montura: fila visible con enlace de descarga resuelto
    const enlace1 = await screen.findByRole('link', { name: /presupuesto_proveedor\.pdf/i });
    expect(enlace1).toHaveAttribute('href', 'blob:mock-object-url');
    expect(enlace1).toHaveAttribute('download', 'presupuesto_proveedor.pdf');

    unmount();

    // Segunda montura con el mismo objeto
    render(<AdjuntosManagerOc oc={ocConAdjunto} />, {
      wrapper: createQueryWrapper(),
    });

    const enlace2 = await screen.findByRole('link', { name: /presupuesto_proveedor\.pdf/i });
    expect(enlace2).toHaveAttribute('href', 'blob:mock-object-url');
    expect(enlace2).toHaveAttribute('download', 'presupuesto_proveedor.pdf');
  });

  it('sin el permiso compras.ordenes.adjuntar no muestra la zona de subida ni file input', () => {
    useAuthStore.setState({
      permisos: ['compras.ordenes.leer'],
    });

    const { container } = render(<AdjuntosManagerOc oc={ocBase} />, {
      wrapper: createQueryWrapper(),
    });

    // Zona de drop y selector no deben renderizarse
    expect(screen.queryByText(/arrastra un archivo aquí/i)).toBeNull();
    expect(screen.queryByText(/seleccionar archivo/i)).toBeNull();
    expect(container.querySelector('input[type="file"]')).toBeNull();

    // Solo se muestra el estado sin adjuntos
    expect(screen.getByText('Sin adjuntos.')).toBeInTheDocument();
  });
});
