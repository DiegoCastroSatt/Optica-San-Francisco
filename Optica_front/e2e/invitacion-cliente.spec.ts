import { expect, test, type Page } from '@playwright/test';
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
  messages: Array<{ ID: string }>;
}

// Cliente creado por el administrador: recibe la invitación, define su contraseña e inicia sesión.
test('cliente creado por el administrador activa su cuenta desde el correo e inicia sesión', async ({ browser, page, request }) => {
  test.setTimeout(120_000);
  const idEjecucion = Date.now();
  const correoCliente = `invitado-e2e-${idEjecucion}@optica.invalid`;
  const rut = crearRutE2E(randomInt(10_000_000, 90_000_000));
  // Contraseña exclusiva de esta prueba, generada por ejecución.
  const contrasenaCliente = `Invitado-${idEjecucion}!`;

  await test.step('el administrador crea el cliente y se envía la invitación', async () => {
    await page.goto('/admin');
    await page.getByLabel('RUT, correo electrónico o nombre de usuario').fill('e2e-admin');
    await page.getByRole('textbox', { name: 'Contraseña' }).fill(contrasenaAdministrador);
    const login = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Autenticacion/login` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Ingresar' }).click();
    expect((await login).status(), 'El administrador sintético debe iniciar sesión').toBe(200);
    await expect(page).toHaveURL(/\/admin\/panel/);

    await page.goto('/admin/panel/clientes');
    await expect(page.getByRole('heading', { name: 'Gestión de clientes' })).toBeVisible();
    await page.getByPlaceholder('12345678-5 o 12.345.678-K').fill(rut);
    await page.getByPlaceholder('Ej. María').fill('Invitada');
    await page.getByPlaceholder('Ej. González').fill('Prueba');
    await page.getByPlaceholder('+56 9 1234 5678').fill('+56 9 1234 5678');
    await page.getByPlaceholder('tu@correo.cl').fill(correoCliente);

    const crear = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Clientes` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Guardar Cliente' }).click();
    const respuestaCrear = await crear;
    expect(respuestaCrear.status(), 'POST /api/Clientes debe registrar al cliente').toBe(201);
    const creado: { idCliente: number; invitacionEnviada: boolean } = await respuestaCrear.json();
    expect(creado.idCliente).toBeGreaterThan(0);
    expect(creado.invitacionEnviada, 'El alta debe informar que se envió la invitación').toBe(true);
    await expect(page.getByRole('status')).toContainText('Se envió al cliente un correo para crear su contraseña.');
  });

  let enlaceActivacion = '';
  await test.step('el correo "Activa tu cuenta" llega a Mailpit con el enlace de activación', async () => {
    let mensaje: MensajeMailpit | undefined;
    await expect.poll(async () => {
      const busquedaRespuesta = await request.get(`${urlMailpit}/api/v1/search?query=to:${encodeURIComponent(correoCliente)}`);
      if (!busquedaRespuesta.ok()) return 'Mailpit API no disponible';
      const busqueda: BusquedaMailpit = await busquedaRespuesta.json();
      const id = busqueda.messages[0]?.ID;
      if (!id) return 'Correo todavía no recibido';
      const detalle = await request.get(`${urlMailpit}/api/v1/message/${id}`);
      if (!detalle.ok()) return 'Mensaje aún no disponible';
      mensaje = await detalle.json();
      return mensaje.To.some(destinatario => destinatario.Address.toLowerCase() === correoCliente)
        ? 'correo recibido'
        : 'destinatario incorrecto';
    }, {
      message: 'La invitación se envía al crear el cliente y debe llegar a Mailpit en pocos segundos',
      timeout: 30_000,
      intervals: [500, 1_000, 2_000],
    }).toBe('correo recibido');

    expect(mensaje?.Subject).toContain('Activa tu cuenta');
    const contenido = `${mensaje?.Text ?? ''}\n${mensaje?.HTML ?? ''}`;
    const enlace = contenido.match(/https?:\/\/localhost:4200\/cliente\/activar-cuenta\?token=[0-9a-f]+/)?.[0];
    expect(enlace, 'El correo debe incluir el enlace de activación').toBeDefined();
    enlaceActivacion = enlace ?? '';
  });

  // El cliente usa un contexto separado para no compartir la sesión del administrador.
  const contextoCliente = await browser.newContext();
  const paginaCliente = await contextoCliente.newPage();
  try {
    await test.step('antes de activar, el cliente no puede iniciar sesión', async () => {
      expect(await iniciarSesionCliente(paginaCliente, rut, contrasenaCliente)).toBe(401);
      await expect(paginaCliente).toHaveURL(/\/cliente\/login/);
    });

    await test.step('el cliente define su contraseña desde el enlace del correo', async () => {
      await paginaCliente.goto(enlaceActivacion);
      await expect(paginaCliente.getByRole('heading', { name: 'Activa tu cuenta' })).toBeVisible();
      await paginaCliente.getByLabel('Nueva contraseña').fill(contrasenaCliente);
      await paginaCliente.getByLabel('Confirmar contraseña').fill(contrasenaCliente);
      const activar = paginaCliente.waitForResponse(response =>
        response.url() === `${urlApi}/api/Autenticacion/cliente/activar-cuenta` && response.request().method() === 'POST',
      );
      await paginaCliente.getByRole('button', { name: 'Crear contraseña' }).click();
      expect((await activar).status(), 'La activación con el token del correo debe aceptarse').toBe(200);
      await expect(paginaCliente.getByRole('heading', { name: 'Cuenta activada' })).toBeVisible();
    });

    await test.step('el cliente inicia sesión con su RUT y la nueva contraseña', async () => {
      expect(await iniciarSesionCliente(paginaCliente, rut, contrasenaCliente)).toBe(200);
      await expect(paginaCliente).toHaveURL('http://localhost:4200/cliente/inicio');
      await expect(paginaCliente.getByRole('heading', { name: 'Portal del cliente' })).toBeVisible();
    });

    await test.step('el enlace de activación es de un solo uso', async () => {
      await paginaCliente.goto(enlaceActivacion);
      await paginaCliente.getByLabel('Nueva contraseña').fill(`Otra-${contrasenaCliente}`);
      await paginaCliente.getByLabel('Confirmar contraseña').fill(`Otra-${contrasenaCliente}`);
      const reintento = paginaCliente.waitForResponse(response =>
        response.url() === `${urlApi}/api/Autenticacion/cliente/activar-cuenta` && response.request().method() === 'POST',
      );
      await paginaCliente.getByRole('button', { name: 'Crear contraseña' }).click();
      expect((await reintento).status()).toBe(400);
      await expect(paginaCliente.getByRole('alert')).toContainText('El enlace no es válido, venció o ya fue utilizado.');
    });
  } finally {
    await contextoCliente.close();
  }
});

async function iniciarSesionCliente(page: Page, identificador: string, contrasena: string): Promise<number> {
  await page.goto('/cliente/login');
  await page.getByLabel('RUT, correo electrónico o nombre de usuario').fill(identificador);
  await page.getByRole('textbox', { name: 'Contraseña' }).fill(contrasena);
  const login = page.waitForResponse(response =>
    response.url() === `${urlApi}/api/Autenticacion/login` && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Ingresar' }).click();
  return (await login).status();
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
