import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';

export const MOTIVO_BAJA_MIN = 5;
export const MOTIVO_BAJA_MAX = 500;

interface MotivoBajaDialogProps {
  nombreArchivo: string;
  /** Lanza para mostrar el error en el diálogo (se queda abierto). */
  onConfirmar: (motivo: string) => Promise<void>;
  onCerrar: () => void;
  /** Mensaje de error ya traducido por el caller. */
  error: string | null;
  /** Textos para reusar el diálogo en otras acciones con motivo (default: baja de documento). */
  titulo?: string;
  descripcion?: string;
  etiqueta?: string;
  accion?: string;
  variante?: 'secondary-danger' | 'default';
  /** Mínimo de caracteres del motivo (default MOTIVO_BAJA_MIN). */
  minimo?: number;
}

/**
 * Baja lógica con motivo obligatorio (5-500). La baja es irreversible y
 * el archivo físico se conserva (contrato G1.2). Envoltura de
 * <c>MotivoDialog</c> con los textos de baja de documento.
 */
export function MotivoBajaDialog({
  nombreArchivo,
  titulo = 'Dar de baja el documento',
  descripcion,
  etiqueta = 'Motivo de la baja',
  accion = 'Dar de baja',
  minimo = MOTIVO_BAJA_MIN,
  ...resto
}: MotivoBajaDialogProps) {
  return (
    <MotivoDialog
      {...resto}
      titulo={titulo}
      descripcion={
        descripcion ??
        `${nombreArchivo}. La baja es irreversible; el archivo se conserva para auditoría pero deja de estar disponible para descarga.`
      }
      etiqueta={etiqueta}
      verbo={accion}
      min={minimo}
      max={MOTIVO_BAJA_MAX}
    />
  );
}
