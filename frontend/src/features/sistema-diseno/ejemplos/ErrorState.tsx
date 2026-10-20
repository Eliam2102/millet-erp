import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { useState } from 'react';

export default function Ejemplo() {
  const [intentos, setIntentos] = useState(0);
  return (
    <div>
      <ErrorState
        title="No se pudo cargar el ejemplo"
        problem={{
          type: 'about:blank',
          title: 'Fallo simulado',
          status: 503,
          detail: 'La carga simulada se interrumpió. Pulsa Reintentar para restablecerla.',
        }}
        onRetry={() => setIntentos((n) => n + 1)}
      />
      <p role="status">Reintentos locales: {intentos}</p>
    </div>
  );
}
