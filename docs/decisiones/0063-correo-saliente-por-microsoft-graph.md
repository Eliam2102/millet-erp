# ADR-0063: Correo saliente por Microsoft Graph desde el buzón del ERP

- **Estado**: Propuesta (la aprueba el owner del proyecto)
- **Fecha**: 2026-10-31
- **Decisores**: por aprobar
- **Etiquetas**: email, notificaciones, infraestructura, graph
- **Reemplaza parcialmente**: [ADR-0026](./0026-notificaciones-email.md), solo en el proveedor de envío.

## Contexto y problema

[ADR-0026](./0026-notificaciones-email.md) eligió Azure Communication Services
Email como proveedor del correo del ERP. Ese recurso nunca se creó. Mientras
tanto, los módulos quedaron con transportes distintos:

- **Simulados (NoOp):** Facturación (CFDI al cliente), Compras (OC al
  proveedor) y Almacén (avisos).
- **Graph propio:** CxP (avisos de revisión y portal de proveedores) e
  Identidad (correo de acceso), cada uno con su propio cliente.
- **SMTP propio a Mailpit:** CxC (estado de cuenta).

El tenant de Millet ya tiene el permiso `Mail.Send` concedido a la aplicación
de la API, limitado en Exchange al buzón dedicado del ERP (acuerdo de octubre
de 2026). Con eso, los correos pueden salir del dominio de Millet sin crear
recursos nuevos en Azure.

## Drivers de la decisión

- Que los documentos y avisos lleguen de verdad, desde el dominio de Millet.
- No crear recursos de Azure ni configurar dominios nuevos (SPF/DKIM) para empezar.
- Un solo punto de envío: misma configuración, mismo modo pruebas y mismo registro para todos los módulos.
- Que un fallo del proveedor no se pierda ni marque el documento como enviado.
- El servidor de pruebas tiene clientes y proveedores reales: ningún correo de prueba puede llegarles.

## Opciones consideradas

1. Azure Communication Services Email (ADR-0026).
2. Microsoft Graph `sendMail` desde el buzón del ERP, con la credencial de la API.
3. SMTP autenticado contra Exchange Online.

## Decisión

Se usa la **opción 2**. Hay un solo puerto, `ICorreoSaliente`
(`SharedKernel/Application/Correo`), y la sección `Correo` lo configura:

- `Proveedor`:
  - `NoOp`: no sale correo; es el valor por omisión.
  - `SandboxLocal`: Mailpit en loopback, solo en Development.
  - `Graph`: buzón del ERP, con la credencial `Auth:EntraId` y `ClientSecret` desde Key Vault.
- **Validación al arrancar.** Si se pide `Graph` y falta el remitente, el
  tenant, el cliente o el secreto, la API no arranca y nombra la clave que
  falta.
- **Modo pruebas** (`Correo:ModoPruebas`). Cada destinatario fuera de
  `DominiosPermitidos` se cambia por `BuzonesPrueba`. El asunto lleva
  «[PRUEBAS]» y el cuerpo, el destinatario original.
- **Registro.** Cada intento queda en `core.correo_envio`: módulo, tipo,
  referencia, destinatario original y real, asunto, resultado (aceptado,
  error con código y motivo, o simulado), adjuntos y fecha. No se guardan los
  adjuntos.
- **Reintento.** Lo decide el módulo dueño del envío, que ya tiene su
  estado: la bitácora del CFDI con su worker, el envío de la OC y el envío
  del estado de cuenta. El transporte devuelve el error y no lo oculta.
- **Adjuntos.** Van en el mismo correo hasta 3 MB en total, que es el
  límite del envío simple de Graph. Si pasan de 3 MB, el envío queda en
  error `CORREO_ADJUNTOS_EXCEDEN_3MB` (`PLATFORM-TODO(<CorreoEnlaceDescarga>)`).

De ADR-0026 se conserva la idea de un puerto único, trazabilidad y reintento.
Lo que cambia es el proveedor y el lugar del reintento: en lugar de un outbox
central, lo hace cada módulo sobre su propio estado.

## Consecuencias

**Positivas**
- No se crean recursos en Azure. El remitente es el buzón del ERP, en el dominio de Millet.
- Facturación, Compras, CxC, Almacén y CxP usan el mismo transporte, con el mismo modo pruebas y el mismo registro.
- Con Graph, las pantallas dejan de decir «envío simulado» (`EsSimulado` sigue al proveedor).

**Negativas**
- Los límites de Exchange Online valen para el buzón del ERP: unos 10,000 destinatarios al día y 30 mensajes por minuto.
- Graph confirma que aceptó la solicitud, no que el correo llegó al buzón. No hay manejo de rebotes.
- El reintento no está centralizado: cada módulo nuevo debe guardar su estado de envío.

## Descartadas

- **ACS Email:** obliga a crear el recurso, verificar el dominio y configurar SPF/DKIM antes del primer envío. El permiso de Graph ya estaba concedido.
- **SMTP autenticado:** Microsoft lo está retirando para el envío desde aplicaciones y exigiría otra credencial.

## Notas de implementación

- Hecho en 86e3txv0n:
  - el puerto `ICorreoSaliente`, `CorreoEnvio` y `core.correo_envio`;
  - los adaptadores Graph, SandboxLocal, NoOp y uno en memoria para pruebas;
  - los consumidores de Facturación, Compras, CxC, Almacén, y avisos de revisión y portal de CxP.
- **El correo de acceso de Identidad no pasa por este transporte.** Lleva
  la contraseña temporal y el modo pruebas la redirigiría a otros buzones.
  Cuando se migre, debe ir **sin** modo pruebas: el correo de acceso nunca se
  redirige.
- Siguen pendientes de conectar al mismo puerto los PLATFORM-TODO
  `<Notificaciones>` de CxC (alertas de cartera), Compras (incidencias) y CxP
  (OC cancelada, NC en espera).
- Falta el aviso interno en pantalla, sin correo: hoy no hay infraestructura de notificaciones en la aplicación.
- Hecho en 86e3zr74h (límites confirmados por Jorge Toache, 10-nov-2026: el
  buzón erpmillet@ usa los límites estándar de Exchange Online):
  - `Correo:Limites` controla el volumen con valores abajo de los de Exchange:
    20 mensajes por minuto (el envío espera turno), 300 destinatarios por
    mensaje (más se rechaza con `CORREO_EXCEDE_DESTINATARIOS`) y 8,000
    destinatarios en 24 h (`CORREO_LIMITE_DIARIO`). La API no arranca con
    valores arriba de 30/min, 500/mensaje o 10,000/24 h. Los contadores son
    por instancia y se reinician con la API.
  - 429, 503, `550 5.7.233` (límite de destinatarios externos de la
    organización) y los códigos de cuota de Exchange se clasifican como
    `CORREO_LIMITE_PROVEEDOR` y se reintentan con espera de 10, 20 y 40 s. Si
    se agotan, el error vuelve al módulo dueño, que conserva su envío
    (Facturación lo deja `Fallido` para su worker y el reenvío manual).
  - Solo se detecta el `5.7.233` que Graph devuelve al enviar. Si Exchange lo
    manda después como rebote (NDR) al buzón, el ERP no lo ve: sigue sin
    manejo de rebotes.
  - `Correo:SecretoVenceEl` guarda la fecha de vencimiento del client secret
    con Mail.Send (2027-01-27; el valor sigue en Key Vault). Con
    `Proveedor=Graph`, desde `AvisoSecretoDiasAntes` días antes (30) se
    escribe un aviso diario en el log y el health check `correo-secreto-graph`
    queda degradado; vencido, queda no sano. Al renovar, actualizar la fecha.
- La creación automática de cuentas en Entra queda apagada: Millet negó
  User.ReadWrite.All (tenant Entra Free). TI crea las cuentas a mano y el ERP
  solo asigna empleado, rol y sucursal.
