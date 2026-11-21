import { Component } from '@angular/core';

/**
 * The place of something that is on its way: a block that pulses. Its size is the caller's,
 * who knows what will stand there. It is a drawing, hidden from screen readers: the screen
 * says in words what it waits for.
 */
@Component({
  selector: 'rs-skeleton',
  host: { 'aria-hidden': 'true' },
  template: ``,
  styleUrl: './skeleton.css',
})
export class Skeleton {}
