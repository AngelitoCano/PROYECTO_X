# Manual de usuario

## Cliente
1. Entra en `http://localhost:8088`.
2. Revisa el catálogo y utiliza el buscador.
3. Pulsa un reloj y luego «Añadir a la cesta».
4. Abre el carrito en el encabezado.
5. Completa los datos ficticios de envío y elige una forma de pago simulada.
6. Crea el pedido y espera el resultado de la verificación de stock.
7. Cuando aparezca «Pendiente de pago», simula aprobación o rechazo.
8. Comprueba que el estado se actualiza.

## Administrador (solo demo local)
1. Pulsa «Administración».
2. Revisa el inventario y los pedidos recientes.
3. Usa los botones `+1` y `-1` para ajustar existencias.
4. Rellena «Agregar reloj» para registrar otro modelo.
5. Revisa movimientos para comprobar las salidas por venta.

## Errores habituales
- «No se pudo conectar»: esperar a que Docker levante Kafka y las APIs.
- «Stock insuficiente»: seleccionar otra cantidad o ajustar existencias.
- Pago deshabilitado: el pedido aún está esperando la reserva de inventario.
- Tras reiniciar: los datos permanecen si no se borran los volúmenes.

No usar datos personales reales: esta aplicación no dispone de controles de seguridad de producción.
