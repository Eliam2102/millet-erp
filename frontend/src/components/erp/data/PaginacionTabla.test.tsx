import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PaginacionTabla } from './PaginacionTabla';

describe('PaginacionTabla', () => {
  it('muestra rango y deshabilita Anterior en la primera página', () => {
    render(<PaginacionTabla total={214} offset={0} limit={25} onChange={vi.fn()} />);
    expect(screen.getByText('1–25 de 214')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Página siguiente' })).toBeEnabled();
  });

  it('última página: hasta = total y Siguiente deshabilitado', () => {
    render(<PaginacionTabla total={214} offset={200} limit={25} onChange={vi.fn()} />);
    expect(screen.getByText('201–214 de 214')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Página siguiente' })).toBeDisabled();
  });

  it('0 resultados', () => {
    render(<PaginacionTabla total={0} offset={0} limit={25} onChange={vi.fn()} />);
    expect(screen.getByText('0 de 0')).toBeInTheDocument();
  });

  it('navega y cambia tamaño volviendo a offset 0', () => {
    const onChange = vi.fn();
    render(<PaginacionTabla total={214} offset={25} limit={25} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(onChange).toHaveBeenLastCalledWith(50, 25);
    fireEvent.click(screen.getByRole('button', { name: 'Página anterior' }));
    expect(onChange).toHaveBeenLastCalledWith(0, 25);
    fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: '50' } });
    expect(onChange).toHaveBeenLastCalledWith(0, 50);
  });

  it('tamanos vacío oculta el selector', () => {
    render(<PaginacionTabla total={5} offset={0} limit={25} onChange={vi.fn()} tamanos={[]} />);
    expect(screen.queryByLabelText('Filas por página')).toBeNull();
  });

  it('sin extremos no muestra inicio/final', () => {
    render(<PaginacionTabla total={214} offset={25} limit={25} onChange={vi.fn()} />);
    expect(screen.queryByRole('button', { name: 'Ir al inicio' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Ir al final' })).toBeNull();
  });

  it('extremos: salta al inicio y a la última página', () => {
    const onChange = vi.fn();
    render(<PaginacionTabla total={214} offset={25} limit={25} onChange={onChange} extremos />);
    fireEvent.click(screen.getByRole('button', { name: 'Ir al final' }));
    expect(onChange).toHaveBeenLastCalledWith(200, 25);
    fireEvent.click(screen.getByRole('button', { name: 'Ir al inicio' }));
    expect(onChange).toHaveBeenLastCalledWith(0, 25);
  });

  it('extremos: total múltiplo del tamaño cae en la última página completa', () => {
    const onChange = vi.fn();
    render(<PaginacionTabla total={50} offset={0} limit={25} onChange={onChange} extremos />);
    expect(screen.getByRole('button', { name: 'Ir al inicio' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Ir al final' }));
    expect(onChange).toHaveBeenLastCalledWith(25, 25);
  });

  it('extremos se deshabilitan en los bordes', () => {
    render(<PaginacionTabla total={30} offset={20} limit={10} onChange={vi.fn()} extremos />);
    expect(screen.getByRole('button', { name: 'Ir al final' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Ir al inicio' })).toBeEnabled();
  });
});
