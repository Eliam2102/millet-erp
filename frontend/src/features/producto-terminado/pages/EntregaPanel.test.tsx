import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { EntregaPanel } from './EntregaPanel';
import type { AlbaranDto, EntregaDto } from '../api/types';

const entrega = vi.hoisted(() => ({ mutateAsync: vi.fn(), corregir: vi.fn() }));
vi.mock('../api/hooks', () => ({
  useConfirmarEntrega: () => ({ mutateAsync: entrega.mutateAsync, isPending: false }),
  useCorregirEntrega: () => ({ mutateAsync: entrega.corregir, isPending: false }),
}));
// jsdom no dibuja: el lienzo de firma se sustituye por un botón que entrega un PNG.
vi.mock('./FirmaPad', () => ({
  FirmaPad: ({ onChange }: { onChange: (v: string | null) => void }) => <button type="button" onClick={() => onChange('data:image/png;base64,AAAA')}>Firmar</button>,
}));
vi.mock('../lib/imagen', () => ({ reducirFoto: async () => 'data:image/jpeg;base64,BBBB' }));

const albaran = {
  id: 'a1', folio: 'HS/1', secuencia: 1, numeroPedido: '260317', cliente: 'Aluminios y Cristales del Sureste', destino: 'Plaza Altabrisa', emitidoAt: null, total: 3,
  lineas: [], etiquetas: ['260317-1', '260317-2', '260317-3'],
} as AlbaranDto;

const montar = (onListo = vi.fn()) => render(<EntregaPanel hojaId="h1" albaran={albaran} onListo={onListo} onCancelar={vi.fn()} />);

beforeEach(() => {
  vi.clearAllMocks();
  entrega.mutateAsync.mockResolvedValue({});
});

describe('EntregaPanel', () => {
  it('sin firma ni foto no deja confirmar y dice qué falta (PT5.6-a)', () => {
    montar();
    fireEvent.change(screen.getByPlaceholderText('Nombre de quien recibe'), { target: { value: 'Mariana Pech' } });
    expect(screen.getByRole('button', { name: /Confirmar entrega/ })).toBeDisabled();
    expect(screen.getByRole('note')).toHaveTextContent('la firma o una foto');
  });

  it('entrega completa con firma manda el recibe, la firma y sin rechazos', async () => {
    const onListo = vi.fn();
    montar(onListo);
    fireEvent.change(screen.getByPlaceholderText('Nombre de quien recibe'), { target: { value: ' Mariana Pech ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Firmar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar entrega · Entrega completa' }));
    await waitFor(() => expect(entrega.mutateAsync).toHaveBeenCalledWith({
      hojaId: 'h1', albaranId: 'a1',
      body: { receptor: 'Mariana Pech', rechazadas: [], motivoRechazo: null, firma: 'data:image/png;base64,AAAA', foto: null, cobro: null },
    }));
    expect(onListo).toHaveBeenCalled();
  });

  it('rechazar piezas pide el motivo y la entrega queda parcial; con foto basta como evidencia', async () => {
    montar();
    fireEvent.change(screen.getByPlaceholderText('Nombre de quien recibe'), { target: { value: 'Mariana Pech' } });
    fireEvent.click(screen.getByLabelText('Rechazar 260317-3'));
    fireEvent.change(screen.getByLabelText('Foto de la entrega'), { target: { files: [new File(['x'], 'f.jpg', { type: 'image/jpeg' })] } });
    await screen.findByAltText('Foto de la entrega');
    const boton = screen.getByRole('button', { name: 'Confirmar entrega · Entrega parcial: 2 de 3' });
    expect(boton).toBeDisabled();
    expect(screen.getByRole('note')).toHaveTextContent('el motivo del rechazo');
    fireEvent.change(screen.getByPlaceholderText(/vidrio con rayón/), { target: { value: 'Vidrio con rayón' } });
    fireEvent.click(boton);
    await waitFor(() => expect(entrega.mutateAsync.mock.calls[0][0].body).toMatchObject({ rechazadas: ['260317-3'], motivoRechazo: 'Vidrio con rayón', firma: null, foto: 'data:image/jpeg;base64,BBBB' }));
  });

  it('el cobro con TPV viaja con su monto y su voucher; un error del servidor se muestra', async () => {
    entrega.mutateAsync.mockRejectedValueOnce(new Error('falló'));
    montar();
    fireEvent.change(screen.getByPlaceholderText('Nombre de quien recibe'), { target: { value: 'Mariana Pech' } });
    fireEvent.click(screen.getByRole('button', { name: 'Firmar' }));
    fireEvent.click(screen.getByLabelText('Cobré en la entrega'));
    fireEvent.change(screen.getByPlaceholderText('8500.00'), { target: { value: '8500' } });
    fireEvent.change(screen.getByPlaceholderText('Folio de la terminal'), { target: { value: 'VCH-0417' } });
    fireEvent.click(screen.getByRole('button', { name: /Confirmar entrega/ }));
    await waitFor(() => expect(entrega.mutateAsync.mock.calls[0][0].body.cobro).toEqual({ monto: 8500, formaPago: 'TPV', referencia: 'VCH-0417' }));
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('corregir (M-248) parte de lo registrado, pide el motivo y no ofrece cobro', async () => {
    entrega.corregir.mockResolvedValue({});
    const onListo = vi.fn();
    const previa = { id: 'e1', resultado: 'Entregada', receptor: 'Mariana Pech', entregadaAt: '2026-11-10T10:00:00Z', entregadas: 3, rechazadas: 0, motivoRechazo: null, tieneFirma: true, tieneFoto: false, etiquetasRechazadas: [], cobro: null } as EntregaDto;
    render(<EntregaPanel hojaId="h1" albaran={albaran} corregir={previa} onListo={onListo} onCancelar={vi.fn()} />);
    expect(screen.getByPlaceholderText('Nombre de quien recibe')).toHaveValue('Mariana Pech');
    expect(screen.queryByLabelText('Cobré en la entrega')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Firmar' }));
    expect(screen.getByRole('button', { name: /Guardar corrección/ })).toBeDisabled();
    fireEvent.click(screen.getByLabelText('Rechazar 260317-3'));
    fireEvent.change(screen.getByPlaceholderText(/Vidrio|vidrio con rayón/i), { target: { value: 'Rayón' } });
    fireEvent.change(screen.getByPlaceholderText(/se capturó mal/), { target: { value: 'Se marcó la pieza equivocada' } });
    fireEvent.click(screen.getByRole('button', { name: /Guardar corrección/ }));
    await waitFor(() => expect(entrega.corregir).toHaveBeenCalledWith({
      hojaId: 'h1', albaranId: 'a1',
      body: { receptor: 'Mariana Pech', rechazadas: ['260317-3'], motivoRechazo: 'Rayón', firma: 'data:image/png;base64,AAAA', foto: null, motivoCorreccion: 'Se marcó la pieza equivocada' },
    }));
    expect(onListo).toHaveBeenCalled();
    expect(entrega.mutateAsync).not.toHaveBeenCalled();
  });
});
