import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DesbloquearLineaDialog } from './DesbloquearLineaDialog';
import { RechazarAjusteDialog } from './RechazarAjusteDialog';

describe('Motivos obligatorios de crédito y ajustes', () => {
  it.each(['desbloquear', 'rechazar'])('explica el bloqueo al %s y conserva el motivo enviado', (accion) => {
    const confirmar = vi.fn();
    render(accion === 'desbloquear'
      ? <DesbloquearLineaDialog open onOpenChange={() => {}} etiquetaLinea="[CLIENTE] · MXN" onConfirm={confirmar} />
      : <RechazarAjusteDialog open onOpenChange={() => {}} onConfirm={confirmar} />);
    const boton = screen.getByRole('button', { name: accion === 'desbloquear' ? 'Desbloquear línea' : 'Rechazar ajuste' });
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute('title', 'Escribe el motivo para continuar.');
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '  Acuerdo revisado  ' } });
    expect(boton).toBeEnabled();
    fireEvent.click(boton);
    expect(confirmar).toHaveBeenCalledWith('Acuerdo revisado');
  });
});
