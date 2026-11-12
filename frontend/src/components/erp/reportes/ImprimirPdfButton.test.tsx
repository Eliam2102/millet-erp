import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import * as impresion from './imprimir-pdf';
import { ImprimirPdfButton } from './ImprimirPdfButton';

afterEach(() => vi.restoreAllMocks());

it('bloquea clics repetidos y vuelve a permitir imprimir al terminar', async () => {
  let completar!: () => void;
  const abrir = vi.spyOn(impresion, 'abrirPdfParaImprimir').mockReturnValue(new Promise<void>(resolve => { completar = resolve; }));
  render(<ImprimirPdfButton generarPdf={vi.fn()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Imprimir' }));
  const ocupado = screen.getByRole('button', { name: 'Preparando…' });
  expect(ocupado).toBeDisabled();
  fireEvent.click(ocupado);
  expect(abrir).toHaveBeenCalledOnce();
  await act(async () => completar());
  expect(screen.getByRole('button', { name: 'Imprimir' })).toBeEnabled();
});

it('muestra el error recuperable y conserva la acción para reintentar', async () => {
  vi.spyOn(impresion, 'abrirPdfParaImprimir').mockRejectedValue(new Error('Permite las ventanas emergentes.'));
  const error = vi.spyOn(toast, 'error').mockImplementation(() => 'toast');
  render(<ImprimirPdfButton generarPdf={vi.fn()} />);
  fireEvent.click(screen.getByRole('button', { name: 'Imprimir' }));
  await waitFor(() => expect(error).toHaveBeenCalledWith('No se pudo preparar la impresión', { description: 'Permite las ventanas emergentes.' }));
  expect(screen.getByRole('button', { name: 'Imprimir' })).toBeEnabled();
});
