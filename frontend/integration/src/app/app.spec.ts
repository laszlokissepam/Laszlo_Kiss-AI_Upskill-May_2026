import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { App } from './app';

type ChatHarness = {
  inputMessage: string;
  sendMessage(): Promise<void>;
};

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should embed the standalone chat component', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('app-chat'))).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.empty-state')?.textContent).toContain(
      'How can I help your garden?'
    );
  });

  it('should send a user message and display the backend response', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const chat = fixture.debugElement.query(By.css('app-chat')).componentInstance as ChatHarness;
    const httpTesting = TestBed.inject(HttpTestingController);

    chat.inputMessage = 'Which plants are suitable for a sunny balcony?';
    const sending = chat.sendMessage();

    const request = httpTesting.expectOne('http://localhost:5078/api/chat');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.message).toBe('Which plants are suitable for a sunny balcony?');
    request.flush({ answer: 'Lavender is a good sunny-balcony option.' });
    await sending;
    await fixture.whenStable();
    fixture.detectChanges();

    const rendered = fixture.nativeElement as HTMLElement;
    expect(rendered.querySelector('.message-user')?.textContent).toContain('sunny balcony');
    expect(rendered.querySelector('.message-bot')?.textContent).toContain('Lavender');
    expect(rendered.querySelector('.empty-state')).toBeNull();
    httpTesting.verify();
  });
});
