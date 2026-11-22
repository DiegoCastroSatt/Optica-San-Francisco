import { expect, test } from '@playwright/test';
import { randomInt } from 'node:crypto';

const contrasenaAdministrador = 'E2E-Admin-Acceso-481!';
const urlApi = 'http://localhost:8080';
const urlMailpit = 'http://localhost:8025';

interface MensajeMailpit {
  ID: string;
  To: Array<{ Address: string }>;
  Subject: string;
  Text: string;
  HTML: string;
}

interface BusquedaMailpit {
  messages: Array<{ ID: string; Subject: string }>;
}

// Pedido marcado como listo: el cliente recibe el correo con el resumen del pedido.
test('al marcar un pedido como listo, el cliente recibe el correo con número, estado, fechas y monto', async ({ page, request }) => {
  // El correo se procesa en segundo plano aproximadamente cada minuto.
  test.setTimeout(180_000);
  const idEjecucion = Date.now();
  const correoCliente = `pedido-listo-e2e-${idEjecucion}@optica.invalid`;
  const rut = crearRutE2E(randomInt(10_000_000, 90_000_000));
  const anotacionInterna = `Nota interna E2E ${idEjecucion}`;
  const hoy = new Date();
  const entrega = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() + 7);

  await test.step('el administrador inicia sesión', async () => {
    await page.goto('/admin');
    await page.getByLabel('RUT, correo electrónico o nombre de usuario').fill('e2e-admin');
    await page.getByRole('textbox', { name: 'Contraseña' }).fill(contrasenaAdministrador);
    const login = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Autenticacion/login` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Ingresar' }).click();
    expect((await login).status(), 'El administrador sintético debe iniciar sesión').toBe(200);
    await expect(page).toHaveURL(/\/admin\/panel/);
  });

  await test.step('el administrador registra al cliente', async () => {
    await page.goto('/admin/panel/clientes');
    await expect(page.getByRole('heading', { name: 'Gestión de clientes' })).toBeVisible();
    await page.getByPlaceholder('12345678-5 o 12.345.678-K').fill(rut);
    await page.getByPlaceholder('Ej. María').fill('Pedido');
    await page.getByPlaceholder('Ej. González').fill('Listo');
    await page.getByPlaceholder('+56 9 1234 5678').fill('+56 9 1234 5678');
    await page.getByPlaceholder('tu@correo.cl').fill(correoCliente);
    const crear = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Clientes` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Guardar Cliente' }).click();
    expect((await crear).status(), 'POST /api/Clientes debe registrar al cliente').toBe(201);
  });

  let idPedido = 0;
  await test.step('el administrador crea el pedido desde la cotización', async () => {
    await page.goto('/admin/panel/crear-pedido');
    await expect(page.getByRole('heading', { name: 'Crear Pedido' })).toBeVisible();
    await page.getByPlaceholder('Ingresa el RUT del cliente').fill(rut);
    await page.locator('#fecha').fill(formatoIso(hoy));
    await page.locator('#fechaEntregaEstimada').fill(formatoIso(entrega));
    await page.locator('#productos').fill('Armazón y cristales');
    await page.locator('#anotaciones').fill(anotacionInterna);
    await page.locator('#total').fill('129990');
    await page.getByRole('button', { name: 'Generar Cotización' }).click();
    await expect(page.getByRole('heading', { name: 'Vista previa de la cotización' })).toBeVisible();

    const crearPedido = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Pedidos` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Confirmar Pedido' }).click();
    const respuesta = await crearPedido;
    expect(respuesta.status(), 'POST /api/Pedidos debe crear el pedido').toBe(201);
    idPedido = (await respuesta.json() as { idPedido: number }).idPedido;
    expect(idPedido).toBeGreaterThan(0);
  });

  await test.step('el administrador marca el pedido como listo', async () => {
    await page.goto(`/admin/panel/pedido/${idPedido}`);
    await expect(page.getByRole('heading', { name: 'Detalle del Pedido' })).toBeVisible();
    await page.locator('select.detail-badge').selectOption('Listo');
    await expect(page.getByRole('dialog', { name: 'Confirmar pedido listo' })).toBeVisible();

    const actualizar = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Pedidos/${idPedido}/estado` && response.request().method() === 'PATCH',
    );
    await page.getByRole('button', { name: 'Confirmar que está listo' }).click();
    const respuesta = await actualizar;
    expect(respuesta.status(), 'PATCH del estado debe aceptarse').toBe(200);
    const resultado = await respuesta.json() as { estado: string; estadoNotificacion: string };
    expect(resultado.estado).toBe('Listo');
    expect(resultado.estadoNotificacion, 'El correo debe quedar en la cola de envío').toBe('Pendiente');
  });

  await test.step('el correo de pedido listo llega a Mailpit con el resumen del pedido', async () => {
    const asuntoEsperado = `Pedido #${idPedido} listo para retirar`;
    let mensaje: MensajeMailpit | undefined;
    await expect.poll(async () => {
      const busquedaRespuesta = await request.get(`${urlMailpit}/api/v1/search?query=to:${encodeURIComponent(correoCliente)}`);
      if (!busquedaRespuesta.ok()) return 'Mailpit API no disponible';
      const busqueda: BusquedaMailpit = await busquedaRespuesta.json();
      // El cliente también recibe la invitación de cuenta; se busca solo el correo del pedido.
      const id = busqueda.messages.find(m => m.Subject.includes(asuntoEsperado))?.ID;
      if (!id) return 'Correo todavía no recibido';
      const detalle = await request.get(`${urlMailpit}/api/v1/message/${id}`);
      if (!detalle.ok()) return 'Mensaje aún no disponible';
      mensaje = await detalle.json();
      return mensaje.To.some(destinatario => destinatario.Address.toLowerCase() === correoCliente)
        ? 'correo recibido'
        : 'destinatario incorrecto';
    }, {
      message: 'El procesador de fondo envía la cola de pedidos aproximadamente cada minuto',
      timeout: 120_000,
      intervals: [2_000, 5_000],
    }).toBe('correo recibido');

    const html = mensaje?.HTML ?? '';
    expect(mensaje?.Subject).toContain(asuntoEsperado);
    expect(html).toContain(`#${idPedido}`);
    expect(html).toContain('Listo para retirar');
    expect(html).toContain(formatoCorreo(hoy));
    expect(html).toContain(formatoCorreo(entrega));
    expect(html).toContain('$129.990');
    expect(html, 'Las anotaciones internas no deben enviarse al cliente').not.toContain(anotacionInterna);
  });
});

// Fecha en formato yyyy-MM-dd para los campos de tipo date.
function formatoIso(fecha: Date): string {
  return `${fecha.getFullYear()}-${dosDigitos(fecha.getMonth() + 1)}-${dosDigitos(fecha.getDate())}`;
}

// Fecha en el formato dd-MM-yyyy que usa el correo (cultura es-CL).
function formatoCorreo(fecha: Date): string {
  return `${dosDigitos(fecha.getDate())}-${dosDigitos(fecha.getMonth() + 1)}-${fecha.getFullYear()}`;
}

function dosDigitos(valor: number): string {
  return String(valor).padStart(2, '0');
}

function crearRutE2E(valor: number): string {
  const cuerpo = String((valor % 80_000_000) + 10_000_000);
  let suma = 0;
  let multiplicador = 2;
  for (const digito of [...cuerpo].reverse()) {
    suma += Number(digito) * multiplicador;
    multiplicador = multiplicador === 7 ? 2 : multiplicador + 1;
  }
  const resto = 11 - (suma % 11);
  const verificador = resto === 11 ? '0' : resto === 10 ? 'K' : String(resto);
  return `${cuerpo}-${verificador}`;
}
