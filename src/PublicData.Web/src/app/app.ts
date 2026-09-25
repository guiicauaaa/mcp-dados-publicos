import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { StatusChips } from './status-chips';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, StatusChips],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
