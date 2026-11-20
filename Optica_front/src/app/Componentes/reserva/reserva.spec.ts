import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ReservaComponent } from './reserva';
import { ReservaService } from './reserva.service';
import { AutenticacionClienteService } from '../../shared/services/autenticacion-cliente.service';

describe('Reservas', () => {
  const horarios = [
    { idHorario: 10, fecha: '2026-09-22T00:00:00', hora: '09:00:00' },
    { idHorario: 11, fecha: '2026-09-22T00:00:00', hora: '09:20:00' },
    { idHorario: 12, fecha: '2026-09-23T00:00:00', hora: '10:00:00' }
  ];
  const servicio = { obtenerDisponibles: vi.fn(), crearReserva: vi.fn() };

  beforeEach(async () => {
    vi.resetAllMocks();
    servicio.obtenerDisponibles.mockReturnValue(of(horarios));
    servicio.crearReserva.mockReturnValue(of({ id: 1, fecha: '2026-09-22', hora: '09:00', estado: 'Pendiente', correoEnviado: true }));
    await TestBed.configureTestingModule({
      imports: [ReservaComponent],
      providers: [
        { provide: ReservaService, useValue: servicio },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function preparar() {
    const fixture = TestBed.createComponent(ReservaComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, dom: fixture.nativeElement as HTMLElement };
  }

  it('carga los días disponibles al iniciar', () => {
    const { dom } = preparar();

    expect(servicio.obtenerDisponibles).toHaveBeenCalledTimes(1);
    expect(dom.querySelectorAll('#date option').length).toBe(3);
  });

  it('permite actualizar al instante los horarios liberados por una cancelación', () => {
    const { fixture, component, dom } = preparar();
    const horarioLiberado = { idHorario: 13, fecha: '2026-09-22T00:00:00', hora: '10:00:00' };
    component['reservationForm'].controls.date.setValue('2026-09-22');
    servicio.obtenerDisponibles.mockReturnValueOnce(of([...horarios, horarioLiberado]));

    component['actualizarHorariosDisponibles']();
    fixture.detectChanges();

    expect(servicio.obtenerDisponibles).toHaveBeenCalledTimes(2);
    expect(Array.from(dom.querySelectorAll<HTMLSelectElement>('#time option')).map(opcion => opcion.value))
      .toEqual(['', '10', '11', '13']);
    expect(component['isRefreshingSlots']()).toBe(false);
  });

  it('actualiza las horas cuando cambia el día', () => {
    const { fixture, component, dom } = preparar();
    component['reservationForm'].controls.date.setValue('2026-09-22');
    fixture.detectChanges();

    const opciones = Array.from(dom.querySelectorAll<HTMLSelectElement>('#time option'))
      .map(opcion => opcion.value);
    expect(opciones).toEqual(['', '10', '11']);
  });

  it('impide enviar una reserva incompleta', () => {
    const { component } = preparar();

    component['submitReservation']();

    expect(servicio.crearReserva).not.toHaveBeenCalled();
    expect(component['reservationForm'].touched).toBe(true);
  });

  it('impide enviar una reserva con dígito verificador inválido', () => {
    const { component } = preparar();
    component['reservationForm'].setValue({
      name: 'Ana Pérez',
      rut: '12.345.678-9',
      phone: '+56 9 1234 5678',
      email: 'ana@example.com',
      date: '2026-09-22',
      time: '10'
    });

    component['submitReservation']();

    expect(servicio.crearReserva).not.toHaveBeenCalled();
    expect(component['reservationForm'].controls.rut.hasError('rutInvalido')).toBe(true);
  });

  it('impide enviar una reserva con un teléfono de menos de nueve dígitos', () => {
    const { component } = preparar();
    component['reservationForm'].setValue({
      name: 'Ana Pérez',
      rut: '12.345.678-5',
      phone: '91234567',
      email: 'ana@example.com',
      date: '2026-09-22',
      time: '10'
    });

    component['submitReservation']();

    expect(servicio.crearReserva).not.toHaveBeenCalled();
    expect(component['reservationForm'].controls.phone.hasError('pattern')).toBe(true);
  });

  it('envía una reserva válida y muestra confirmación', () => {
    const { fixture, component, dom } = preparar();
    component['reservationForm'].setValue({
      name: 'Ana Pérez',
      rut: '12.345.678-5',
      phone: '+56 9 1234 5678',
      email: 'ana@example.com',
      date: '2026-09-22',
      time: '10'
    });

    component['submitReservation']();
    fixture.detectChanges();

    expect(servicio.crearReserva).toHaveBeenCalledWith({
      nombreCompleto: 'Ana Pérez',
      rut: '12.345.678-5',
      telefono: '+56 9 1234 5678',
      correo: 'ana@example.com',
      idHorario: 10
    });
    expect(dom.textContent).toContain('Reserva registrada, Ana Pérez');
    expect(dom.textContent).toContain('Te contactaremos para confirmar la disponibilidad de tu hora.');
    expect(dom.textContent).not.toContain('Te enviamos un correo');
  });

  it('muestra el mensaje de la API y no deja un éxito cuando la reserva es rechazada', () => {
    const { fixture, component, dom } = preparar();
    component['reservationForm'].setValue({
      name: 'Ana Pérez', rut: '12.345.678-5', phone: '+56 9 1234 5678',
      email: 'ana@example.com', date: '2026-09-22', time: '10'
    });
    servicio.crearReserva.mockReturnValue(throwError(() => ({
      error: { mensaje: 'Ya tienes una reserva activa para esta fecha. No puedes reservar otra hora el mismo día.' }
    })));

    component['submitReservation']();
    fixture.detectChanges();

    expect(dom.textContent).toContain('Ya tienes una reserva activa para esta fecha');
    expect(dom.textContent).not.toContain('Reserva registrada, Ana Pérez');
  });
  it('prellena y bloquea los datos de contacto cuando el cliente tiene sesión iniciada', () => {
    TestBed.inject(AutenticacionClienteService).cliente.set({ idCliente: 1, nombre: 'Ana', correo: 'ana@example.com' });
    const { fixture, dom } = preparar();

    const peticion = TestBed.inject(HttpTestingController).expectOne('http://localhost:8080/api/Clientes/perfil');
    expect(peticion.request.withCredentials).toBe(true);
    peticion.flush({ rut: '12.345.678-5', nombre: 'Ana', apellido: 'Pérez', telefono: '+56 9 1234 5678', correo: 'ana@example.com', estado: 'Activo' });
    fixture.detectChanges();

    const nombre = dom.querySelector<HTMLInputElement>('#name')!;
    const rut = dom.querySelector<HTMLInputElement>('#rut')!;
    expect(nombre.value).toBe('Ana Pérez');
    expect(rut.value).toBe('12.345.678-5');
    expect(nombre.disabled).toBe(true);
    expect(rut.disabled).toBe(true);
    expect(dom.textContent).toContain('Usamos los datos de tu cuenta');
  });

  it('no pide datos de cuenta cuando nadie inició sesión', () => {
    preparar();
    TestBed.inject(HttpTestingController).expectNone('http://localhost:8080/api/Clientes/perfil');
  });
});
