import { Component, computed, input } from '@angular/core';
import { formatAnswer } from './format';

/** Model answer with paragraphs, lists and bold, rendered from text (no innerHTML). */
@Component({
  selector: 'app-answer-text',
  template: `
    @for (block of blocks(); track $index) {
      @if (block.kind === 'paragraph') {
        <p>
          @for (s of block.segments; track $index) {
            @if (s.bold) {
              <strong>{{ s.text }}</strong>
            } @else {
              {{ s.text }}
            }
          }
        </p>
      } @else if (block.ordered) {
        <ol>
          @for (item of block.items; track $index) {
            <li>
              @for (s of item; track $index) {
                @if (s.bold) {
                  <strong>{{ s.text }}</strong>
                } @else {
                  {{ s.text }}
                }
              }
            </li>
          }
        </ol>
      } @else {
        <ul>
          @for (item of block.items; track $index) {
            <li>
              @for (s of item; track $index) {
                @if (s.bold) {
                  <strong>{{ s.text }}</strong>
                } @else {
                  {{ s.text }}
                }
              }
            </li>
          }
        </ul>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      line-height: 1.6;
    }
    p,
    ul,
    ol {
      margin: 0 0 0.5rem;
    }
    :host > :last-child {
      margin-bottom: 0;
    }
    ul,
    ol {
      padding-left: 1.25rem;
    }
  `,
})
export class AnswerText {
  readonly text = input.required<string>();

  protected readonly blocks = computed(() => formatAnswer(this.text()));
}
