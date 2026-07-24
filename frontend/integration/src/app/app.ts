import { Component } from '@angular/core';
import {
  ChatComponent,
  type ChatSuggestion
} from '../../../chat-component/src/chat/chat';

@Component({
  selector: 'app-root',
  imports: [ChatComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  public readonly chatApiUrl = 'http://localhost:5078/api/chat';
  public readonly deploymentName = 'gpt-4';
  public readonly welcomeTitle = 'How can I help your garden?';
  public readonly welcomeDescription = 'Ask about plants, products, care tips, or store policies.';
  public readonly suggestions: readonly ChatSuggestion[] = [
    {
      label: 'Beginner-friendly plants',
      message: 'Which beginner-friendly plants are in stock?'
    },
    {
      label: 'Lavender care tips',
      message: 'How should I care for lavender?'
    }
  ];
}
