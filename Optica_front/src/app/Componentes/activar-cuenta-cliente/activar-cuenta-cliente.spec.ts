import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ActivarCuentaClienteComponent } from './activar-cuenta-cliente';
import { AutenticacionClienteService } from '../../shared/services/autenticacion-cliente.service';

describe('ActivarCuentaClienteComponent', () => {
  let fixture: ComponentFixture<ActivarCuentaClienteComponent>;
  let component: ActivarCuentaClienteComponent;
  let token: string | null;
  const router = { navigateByUrl: vi.fn() };
  const autenticacion = { activarCuenta: vi.fn() };

  beforeEach(async () => {
    vi.resetAllMocks();
    token = 'token-invitacion';
    autenticacion.activarCuenta.mockReturnValue(of({ mensaje: 'Tu cuenta quedó activa.' }));
    await TestBed.configureTestingModule({
      imports: [ActivarCuentaClienteComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => token } } } },
        { provide: Router, useValue: router },
        { provide: AutenticacionClienteService, useValue: autenticacion }
      ]
    }).compileComponents();
  });

  function crearComponente(): void {
    fixture = TestBed.createComponent(ActivarCuentaClienteComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('envía el token y la nueva contraseña, y muestra el éxito', () => {
    token = ' token-invitacion ';
    crearComponente();
    component.nuevaContrasena = 'Optica123!';
    component.confirmarContrasena = 'Optica123!';

    component.activar();
    fixture.detectChanges();

    expect(autenticacion.activarCuenta).toHaveBeenCalledWith('token-invitacion', 'Optica123!', 'Optica123!');
    expect(component.estado()).toBe('exito');
    expect(fixture.nativeElement.textContent).toContain('Tu cuenta quedó activa.');
  });

  it('valida la contraseña antes de llamar a la API', () => {
    crearComponente();
    component.nuevaContrasena = 'corta';
    component.confirmarContrasena = 'corta';
    component.activar();
    expect(component.error()).toContain('al menos 8 caracteres');

    component.nuevaContrasena = 'Optica123!';
    component.confirmarContrasena = 'Optica123?';
    component.activar();
    expect(component.error()).toBe('Las contraseñas no coinciden.');

    expect(autenticacion.activarCuenta).not.toHaveBeenCalled();
  });

  it('muestra el mensaje de error que responde la API', () => {
    autenticacion.activarCuenta.mockReturnValueOnce(throwError(() => ({ error: { mensaje: 'El enlace venció.' } })));
    crearComponente();
    component.nuevaContrasena = 'Optica123!';
    component.confirmarContrasena = 'Optica123!';

    component.activar();
    fixture.detectChanges();

    expect(component.estado()).toBe('formulario');
    expect(fixture.nativeElement.textContent).toContain('El enlace venció.');
  });

  it('sin token muestra el aviso y permite volver al login', () => {
    token = '   ';
    crearComponente();

    expect(component.estado()).toBe('sin-token');
    expect(fixture.nativeElement.textContent).toContain('no contiene un token');
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/cliente/login');
  });
});
