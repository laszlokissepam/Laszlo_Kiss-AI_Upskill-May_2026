import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

export type ChatMessage = {
  user: string;
  bot: string;
};

type ChatApiResponse = {
  answer: string;
};

type ChatApiRequest = {
  deploymentName: string;
  message: string;
  temperature: number;
  maxTokens: number;
};

@Component({
  selector: 'app-chat',
  imports: [CommonModule, FormsModule],
  templateUrl: './chat.html',
  styleUrl: './chat.css',
})
export class ChatComponent {
  private readonly chatApiUrl = 'http://localhost:5078/api/chat';
  private readonly deploymentName = 'gpt-4';

  public inputMessage = '';
  public messages: ChatMessage[] = [];
  public isSending = false;
  public errorMessage: string | null = null;

  public constructor(private readonly httpClient: HttpClient) {}

  public async sendMessage(): Promise<void> {
    const trimmed = this.inputMessage.trim();
    if (!trimmed || this.isSending) {
      return;
    }

    this.isSending = true;
    this.errorMessage = null;
    this.inputMessage = '';

    const newMessage: ChatMessage = {
      user: trimmed,
      bot: 'Garden Buddy is thinking...'
    };
    this.messages.push(newMessage);

    const payload: ChatApiRequest = {
      deploymentName: this.deploymentName,
      message: trimmed,
      temperature: 0.2,
      maxTokens: 300
    };

    try {
      const response = await firstValueFrom(
        this.httpClient.post<ChatApiResponse>(this.chatApiUrl, payload)
      );

      newMessage.bot = response.answer?.trim().length
        ? response.answer
        : 'I could not generate an answer for this question.';
    } catch {
      newMessage.bot = 'I could not reach the backend chat service right now.';
      this.errorMessage = 'Backend call failed. Make sure the API is running on http://localhost:5078 and DIAL_API_KEY is set.';
    } finally {
      this.isSending = false;
    }
  }
}
