import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ApiError } from '@/lib/api';
import {
  mensajeErrorAdjunto,
  useAdjuntos,
  useDarDeBajaAdjunto,
  useEnlaceAdjunto,
  useExpediente,
  useSubirAdjunto,
  useTiposDocumento,
} from './hooks';
import { EstadoAdjunto, EstadoExpediente } from './types';

const BASE = '/api/v1/datos-maestros/proveedores/p-1';

const adjunto = {
  id: 'a-1',
  estado: EstadoAdjunto.Vigente,
  nombreArchivo: 'csf.pdf',
};

afterEach(() => vi.restoreAllMocks());

describe('capa de datos de adjuntos', () => {
  it('useAdjuntos lista y pide incluirBajas solo cuando se indica', async () => {
    const urls: string[] = [];
    mswServer.use(
      http.get(`*${BASE}/adjuntos`, ({ request }) => {
        urls.push(new URL(request.url).search);
        return HttpResponse.json([adjunto]);
      }),
    );
    const w = createQueryWrapper();
    const a = renderHook(() => useAdjuntos(BASE), { wrapper: w });
    await waitFor(() => expect(a.result.current.data).toHaveLength(1));
    const b = renderHook(() => useAdjuntos(BASE, { incluirBajas: true }), {
      wrapper: w,
    });
    await waitFor(() => expect(b.result.current.data).toHaveLength(1));
    expect(urls).toEqual(['', '?incluirBajas=true']);
  });

  it('useTiposDocumento y useExpediente leen sus rutas', async () => {
    mswServer.use(
      http.get('*/api/v1/adjuntos/tipos', ({ request }) => {
        expect(new URL(request.url).searchParams.get('entidad')).toBe('proveedor');
        return HttpResponse.json([{ id: 't-1', codigo: 'contrato' }]);
      }),
      http.get(`*${BASE}/expediente`, () =>
        HttpResponse.json({
          completo: false,
          faltantes: ['contrato'],
          documentos: [{ estado: EstadoExpediente.Faltante }],
        }),
      ),
    );
    const t = renderHook(() => useTiposDocumento('proveedor'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(t.result.current.data?.[0].codigo).toBe('contrato'));
    const e = renderHook(() => useExpediente(BASE), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(e.result.current.data?.completo).toBe(false));
  });

  it('useSubirAdjunto manda multipart con Idempotency-Key y vigenteHasta', async () => {
    let key: string | null = null;
    let campos: Record<string, string> = {};
    mswServer.use(
      http.post(`*${BASE}/adjuntos`, async ({ request }) => {
        key = request.headers.get('Idempotency-Key');
        const fd = await request.formData();
        campos = {
          tipoDocumentoId: String(fd.get('tipoDocumentoId')),
          vigenteHasta: String(fd.get('vigenteHasta')),
          archivo: fd.get('archivo') ? 'presente' : 'falta',
        };
        return HttpResponse.json(adjunto, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useSubirAdjunto(BASE), {
      wrapper: createQueryWrapper(),
    });
    await act(async () => {
      await result.current.subir({
        archivo: new File(['%PDF'], 'csf.pdf', { type: 'application/pdf' }),
        tipoDocumentoId: 't-1',
        vigenteHasta: '2026-10-31',
      });
    });
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    expect(campos).toEqual({
      tipoDocumentoId: 't-1',
      vigenteHasta: '2026-10-31',
      archivo: 'presente',
    });
  });

  it('useDarDeBajaAdjunto envía el motivo y expone el 422 de segunda baja', async () => {
    let body: unknown = null;
    mswServer.use(
      http.delete(`*${BASE}/adjuntos/a-1`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...adjunto, estado: EstadoAdjunto.Baja });
      }),
      http.delete(`*${BASE}/adjuntos/a-2`, () =>
        HttpResponse.json(
          { type: 'x', title: 'Ya dado de baja', status: 422, code: 'ADJUNTO_YA_DADO_DE_BAJA' },
          { status: 422 },
        ),
      ),
    );
    const { result } = renderHook(() => useDarDeBajaAdjunto(BASE), {
      wrapper: createQueryWrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({ adjuntoId: 'a-1', motivo: 'Documento erróneo' });
    });
    expect(body).toEqual({ motivo: 'Documento erróneo' });

    let error: unknown;
    await act(async () => {
      await result.current
        .mutateAsync({ adjuntoId: 'a-2', motivo: 'Otra vez' })
        .catch((e) => (error = e));
    });
    expect(mensajeErrorAdjunto(error)).toBe('Este documento ya fue dado de baja.');
  });

  it('useEnlaceAdjunto pide el enlace y lo abre de inmediato', async () => {
    mswServer.use(
      http.post(`*${BASE}/adjuntos/a-1/enlace`, () =>
        HttpResponse.json({
          url: '/api/v1/adjuntos/descargas/tok',
          expiraEn: '2026-10-06T12:00:00Z',
        }),
      ),
    );
    let href = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      href = this.href;
    });
    const { result } = renderHook(() => useEnlaceAdjunto(BASE), {
      wrapper: createQueryWrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({ adjuntoId: 'a-1' });
    });
    expect(href).toContain('/api/v1/adjuntos/descargas/tok');
  });
});

describe('mensajeErrorAdjunto', () => {
  const err = (status: number, extra: object = {}) =>
    new ApiError({ type: 't', title: 'Titulo', status, ...extra }, status);

  it('mapea 401/403/404', () => {
    expect(mensajeErrorAdjunto(err(401))).toMatch(/sesión/i);
    expect(mensajeErrorAdjunto(err(403))).toMatch(/permiso/i);
    expect(mensajeErrorAdjunto(err(404))).toMatch(/ya no existe/i);
  });
  it('422 usa el detalle; 400 usa el primer error de validación', () => {
    expect(mensajeErrorAdjunto(err(422, { detail: 'Vigencia pasada' }))).toBe('Vigencia pasada');
    expect(
      mensajeErrorAdjunto(
        err(400, { errores: [{ campo: 'Motivo', codigo: 'x', mensaje: 'Motivo corto' }] }),
      ),
    ).toBe('Motivo corto');
  });
});
