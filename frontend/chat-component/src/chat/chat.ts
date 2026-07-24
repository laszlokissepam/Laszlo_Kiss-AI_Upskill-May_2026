import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

export type ChatMessage = { user: string; bot: string };
export type ChatSuggestion = { label: string; message: string };
type ChatApiResponse = { answer: string };
type ChatApiRequest = {
  deploymentName: string;
  message: string;
  temperature: number;
  maxTokens: number;
};

@Component({
  selector: 'app-chat',
  imports: [FormsModule],
  templateUrl: './chat.html',
  styleUrl: './chat.scss'
})
export class ChatComponent {
  private readonly chatHistory = viewChild<ElementRef<HTMLDivElement>>('chatHistory');

  public readonly chatApiUrl = input.required<string>();
  public readonly deploymentName = input.required<string>();
  public readonly companyName = input.required<string>();
  public readonly welcomeTitle = input.required<string>();
  public readonly welcomeDescription = input.required<string>();
  public readonly suggestions = input.required<readonly ChatSuggestion[]>();
  public inputMessage = '';
  public readonly messages = signal<ChatMessage[]>([]);
  public readonly isSending = signal(false);
  public readonly errorMessage = signal<string | null>(null);

  public constructor(private readonly httpClient: HttpClient) {}

  public useSuggestion(suggestion: string): void {
    this.inputMessage = suggestion;
    void this.sendMessage();
  }

  public async sendMessage(): Promise<void> {
    const trimmed = this.inputMessage.trim();
    if (!trimmed || this.isSending()) return;

    this.isSending.set(true);
    this.errorMessage.set(null);
    this.inputMessage = '';
    const newMessage: ChatMessage = { user: trimmed, bot: 'Garden Buddy is thinking...' };
    this.messages.update(messages => [...messages, newMessage]);
    this.scrollToBottom();

    const payload: ChatApiRequest = {
      deploymentName: this.deploymentName(),
      message: trimmed,
      temperature: 0.2,
      maxTokens: 300
    };

    try {
      const response = await firstValueFrom(
        this.httpClient.post<ChatApiResponse>(this.chatApiUrl(), payload)
      );
      const answer = response.answer?.trim().length
        ? response.answer
        : 'I could not generate an answer for this question.';
      this.updateBotAnswer(newMessage, answer);
    } catch (error: unknown) {
      console.error('Garden Buddy chat request failed.', error);
      this.updateBotAnswer(newMessage, 'Sorry, something went wrong. Please try again later.');
      this.errorMessage.set(null);
    } finally {
      this.isSending.set(false);
    }
  }

  private updateBotAnswer(target: ChatMessage, bot: string): void {
    this.messages.update(messages =>
      messages.map(message => message === target ? { ...message, bot } : message)
    );
    this.scrollToBottom();
  }

  private scrollToBottom(): void {
    setTimeout(() => {
      const history = this.chatHistory()?.nativeElement;
      if (history) {
        history.scrollTop = history.scrollHeight;
      }
    });
  }
}
